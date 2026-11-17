using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Fluent builder for creating and managing Dataverse Annotations (Notes) with file attachments.
/// </summary>
/// <remarks>
/// This type is not thread-safe. Each instance should be used for a single operation and not shared across threads.
/// Terminal methods (e.g., <see cref="CreateAsync"/>, <see cref="UpdateAsync"/>, <see cref="DeleteAsync(CancellationToken)"/>,
/// <see cref="RetrieveAsync(CancellationToken)"/>) mark the builder as consumed; subsequent calls will throw
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class AnnotationBuilder
{
    private readonly IDataverseTransport _client;
    private Guid? _annotationId;
    private Guid? _regardingObjectId;
    private string? _regardingEntityType;
    private string? _regardingEntitySet;
    private Dictionary<string, object>? _regardingAlternateKey;
    private string? _subject;
    private string? _noteText;
    private string? _fileName;
    private string? _mimeType;
    private byte[]? _fileContent;
    private bool _returnRepresentation;
    private SingleUseGuard _guard;

    internal AnnotationBuilder(IDataverseTransport client, Guid? annotationId = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _annotationId = annotationId;
    }

    /// <summary>
    /// Specifies the record that this annotation is regarding (attached to).
    /// </summary>
    /// <param name="entityType">The logical name of the entity (e.g., "account", "contact")</param>
    /// <param name="entityId">The ID of the entity record</param>
    /// <param name="entitySet">The entity set name. When omitted it is the English plural of <paramref name="entityType"/>
    /// (e.g., "opportunity" becomes "opportunities"); pass it for tables whose entity set name differs.</param>
    /// <returns>Current AnnotationBuilder instance for chaining</returns>
    public AnnotationBuilder Regarding(string entityType, Guid entityId, string? entitySet = null)
    {
        _regardingEntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        _regardingEntitySet = entitySet ?? Pluralize(entityType);
        _regardingObjectId = entityId;
        _regardingAlternateKey = null; // Clear alternate keys when using ID
        return this;
    }

    /// <summary>
    /// Specifies the record that this annotation is regarding (attached to) using a type parameter.
    /// </summary>
    /// <typeparam name="T">The entity type. The logical name is the lowercase type name; the entity set name comes from
    /// <see cref="EntitySetAttribute"/> or a registered <see cref="IDataverseNameResolver"/>.</typeparam>
    /// <param name="entityId">The ID of the entity record</param>
    /// <returns>Current AnnotationBuilder instance for chaining</returns>
    /// <exception cref="InvalidOperationException">Thrown when the entity set name of <typeparamref name="T"/> cannot be resolved</exception>
    public AnnotationBuilder Regarding<T>(Guid entityId) where T : class
    {
        _regardingEntitySet = ODataPropertyNameCache.GetEntitySetName<T>();
        _regardingEntityType = InferEntityLogicalName<T>();
        _regardingObjectId = entityId;
        _regardingAlternateKey = null; // Clear alternate keys when using ID
        return this;
    }

    /// <summary>
    /// Specifies the record that this annotation is regarding (attached to) using an alternate key.
    /// </summary>
    /// <param name="entityType">The logical name of the entity (e.g., "account", "contact")</param>
    /// <param name="keyName">The logical name of the alternate key attribute</param>
    /// <param name="keyValue">The value of the alternate key</param>
    /// <param name="entitySet">The entity set name. When omitted it is the English plural of <paramref name="entityType"/>
    /// (e.g., "opportunity" becomes "opportunities"); pass it for tables whose entity set name differs.</param>
    /// <returns>Current AnnotationBuilder instance for chaining</returns>
    public AnnotationBuilder Regarding(string entityType, string keyName, object keyValue, string? entitySet = null)
    {
        _regardingEntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        _regardingEntitySet = entitySet ?? Pluralize(entityType);
        _regardingObjectId = null; // Clear ID when using alternate key
        _regardingAlternateKey = new Dictionary<string, object> { [keyName] = keyValue };
        return this;
    }

    /// <summary>
    /// Specifies the record that this annotation is regarding (attached to) using an alternate key with strongly-typed expression.
    /// </summary>
    /// <typeparam name="T">The entity type. The logical name is the lowercase type name; the entity set name comes from
    /// <see cref="EntitySetAttribute"/> or a registered <see cref="IDataverseNameResolver"/>.</typeparam>
    /// <typeparam name="TProperty">The property type</typeparam>
    /// <param name="propertyExpression">Expression pointing to the property (e.g., x => x.AccountNumber)</param>
    /// <param name="keyValue">The value of the alternate key</param>
    /// <returns>Current AnnotationBuilder instance for chaining</returns>
    /// <exception cref="InvalidOperationException">Thrown when the entity set name of <typeparamref name="T"/> cannot be resolved</exception>
    public AnnotationBuilder Regarding<T, TProperty>(Expression<Func<T, TProperty>> propertyExpression, TProperty keyValue) where T : class
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);

        _regardingEntitySet = ODataPropertyNameCache.GetEntitySetName<T>();
        _regardingEntityType = InferEntityLogicalName<T>();
        _regardingObjectId = null; // Clear ID when using alternate key
        var logicalName = GetJsonPropertyName(propertyExpression);
        _regardingAlternateKey = new Dictionary<string, object> { [logicalName] = keyValue! };
        return this;
    }

    /// <summary>
    /// Adds an additional alternate key attribute for composite key scenarios.
    /// Must be called after Regarding() with an alternate key.
    /// </summary>
    /// <param name="keyName">The logical name of the alternate key attribute</param>
    /// <param name="keyValue">The value of the alternate key</param>
    /// <returns>Current AnnotationBuilder instance for chaining</returns>
    /// <exception cref="InvalidOperationException">Thrown when called before Regarding() with an alternate key</exception>
    public AnnotationBuilder WithAlternateKey(string keyName, object keyValue)
    {
        if (_regardingAlternateKey == null)
        {
            throw new InvalidOperationException(
                "WithAlternateKey() must be called after Regarding() with an alternate key. " +
                "Use Regarding(entityType, keyName, keyValue) or Regarding<T>(expression, keyValue) first.");
        }

        _regardingAlternateKey[keyName] = keyValue;
        return this;
    }

    /// <summary>
    /// Adds an additional alternate key attribute for composite key scenarios using strongly-typed expression.
    /// Must be called after Regarding() with an alternate key.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    /// <typeparam name="TProperty">The property type</typeparam>
    /// <param name="propertyExpression">Expression pointing to the property (e.g., x => x.SecondaryKey)</param>
    /// <param name="keyValue">The value of the alternate key</param>
    /// <returns>Current AnnotationBuilder instance for chaining</returns>
    /// <exception cref="InvalidOperationException">Thrown when called before Regarding() with an alternate key</exception>
    public AnnotationBuilder WithAlternateKey<T, TProperty>(Expression<Func<T, TProperty>> propertyExpression, TProperty keyValue) where T : class
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);

        if (_regardingAlternateKey == null)
        {
            throw new InvalidOperationException(
                "WithAlternateKey() must be called after Regarding() with an alternate key. " +
                "Use Regarding<T>(expression, keyValue) first.");
        }

        var logicalName = GetJsonPropertyName(propertyExpression);
        _regardingAlternateKey[logicalName] = keyValue!;
        return this;
    }

    /// <summary>
    /// Sets the annotation ID for update or delete operations.
    /// </summary>
    /// <param name="annotationId">The ID of the annotation</param>
    /// <returns>Current AnnotationBuilder instance for chaining</returns>
    public AnnotationBuilder WithId(Guid annotationId)
    {
        _annotationId = annotationId;
        return this;
    }

    /// <summary>
    /// Sets the subject (title) of the annotation.
    /// </summary>
    /// <param name="subject">The subject text</param>
    /// <returns>Current AnnotationBuilder instance for chaining</returns>
    public AnnotationBuilder WithSubject(string subject)
    {
        _subject = subject;
        return this;
    }

    /// <summary>
    /// Sets the note text (description) of the annotation.
    /// </summary>
    /// <param name="noteText">The note text</param>
    /// <returns>Current AnnotationBuilder instance for chaining</returns>
    public AnnotationBuilder WithNoteText(string? noteText)
    {
        _noteText = noteText;
        return this;
    }

    /// <summary>
    /// Attaches a file to the annotation.
    /// </summary>
    /// <param name="fileName">The name of the file</param>
    /// <param name="content">The file content as a byte array</param>
    /// <param name="mimeType">The MIME type of the file (optional, will be inferred from file extension if not provided)</param>
    /// <returns>Current AnnotationBuilder instance for chaining</returns>
    public AnnotationBuilder WithFile(string fileName, byte[] content, string? mimeType = null)
    {
        _fileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        _fileContent = content ?? throw new ArgumentNullException(nameof(content));
        _mimeType = mimeType ?? InferMimeType(fileName);
        return this;
    }

    /// <summary>
    /// Attaches a file to the annotation from a stream.
    /// </summary>
    /// <param name="fileName">The name of the file</param>
    /// <param name="stream">The file content as a stream</param>
    /// <param name="mimeType">The MIME type of the file (optional, will be inferred from file extension if not provided)</param>
    /// <returns>Current AnnotationBuilder instance for chaining</returns>
    public AnnotationBuilder WithFile(string fileName, Stream stream, string? mimeType = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return WithFile(fileName, ms.ToArray(), mimeType);
    }

    /// <summary>
    /// Attaches a file to the annotation from a stream asynchronously.
    /// </summary>
    /// <param name="fileName">The name of the file</param>
    /// <param name="stream">The file content as a stream</param>
    /// <param name="mimeType">The MIME type of the file (optional, will be inferred from file extension if not provided)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Current AnnotationBuilder instance for chaining</returns>
    public async Task<AnnotationBuilder> WithFileAsync(string fileName, Stream stream, string? mimeType = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
        return WithFile(fileName, ms.ToArray(), mimeType);
    }

    /// <summary>
    /// Requests the full annotation representation in the response.
    /// </summary>
    /// <param name="returnIt">Whether to return the representation</param>
    /// <returns>Current AnnotationBuilder instance for chaining</returns>
    public AnnotationBuilder ReturnRepresentation(bool returnIt = true)
    {
        _returnRepresentation = returnIt;
        return this;
    }

    /// <summary>
    /// Creates a new annotation in Dataverse.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The created annotation (if ReturnRepresentation was called), otherwise a minimal annotation with just the ID</returns>
    /// <exception cref="InvalidOperationException">Thrown when regarding object is not specified</exception>
    public async Task<Annotation> CreateAsync(CancellationToken ct = default)
    {
        _guard.Use();

        if (string.IsNullOrEmpty(_regardingEntityType))
        {
            throw new InvalidOperationException("Must specify regarding object using Regarding() method.");
        }

        if (_regardingObjectId == null && (_regardingAlternateKey == null || _regardingAlternateKey.Count == 0))
        {
            throw new InvalidOperationException("Must specify regarding object ID or alternate key using Regarding() method.");
        }

        var payload = new JsonObject();

        // Build the regarding reference (using either ID or alternate key)
        if (_regardingAlternateKey != null && _regardingAlternateKey.Count > 0)
        {
            var alternateKeyUrl = BuildRegardingAlternateKeyUrl();
            payload["objectid_" + _regardingEntityType + "@odata.bind"] = $"/{alternateKeyUrl}";
        }
        else
        {
            payload["objectid_" + _regardingEntityType + "@odata.bind"] = $"/{_regardingEntitySet}({_regardingObjectId})";
        }

        if (!string.IsNullOrEmpty(_subject))
            payload["subject"] = _subject;

        if (!string.IsNullOrEmpty(_noteText))
            payload["notetext"] = _noteText;

        if (_fileContent != null && !string.IsNullOrEmpty(_fileName))
        {
            payload["documentbody"] = Convert.ToBase64String(_fileContent);
            payload["filename"] = _fileName;
            payload["mimetype"] = _mimeType;
            payload["isdocument"] = true;
        }

        var headers = _returnRepresentation
            ? new Dictionary<string, string> { ["Prefer"] = "return=representation" }
            : null;

        var response = await _client.ExecuteRequestAsync(
            HttpMethod.Post,
            "annotations",
            payload,
            headers,
            ct
        ).ConfigureAwait(false);

        return JsonSerializer.Deserialize<Annotation>(response.ToJsonString()) 
               ?? new Annotation();
    }

    /// <summary>
    /// Updates an existing annotation in Dataverse.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Task representing the asynchronous operation</returns>
    /// <exception cref="InvalidOperationException">Thrown when annotation ID is not set</exception>
    public async Task UpdateAsync(CancellationToken ct = default)
    {
        _guard.Use();

        if (_annotationId == null)
        {
            throw new InvalidOperationException("AnnotationId must be set for update operations. Use WithId() method.");
        }

        var payload = new JsonObject();

        if (!string.IsNullOrEmpty(_subject))
            payload["subject"] = _subject;

        if (!string.IsNullOrEmpty(_noteText))
            payload["notetext"] = _noteText;

        if (_fileContent != null && !string.IsNullOrEmpty(_fileName))
        {
            payload["documentbody"] = Convert.ToBase64String(_fileContent);
            payload["filename"] = _fileName;
            payload["mimetype"] = _mimeType;
            payload["isdocument"] = true;
        }

        // If-Match: * makes the PATCH update-only; without it Dataverse would create a new
        // annotation when the ID does not exist.
        await _client.ExecuteRequestAsync(
            HttpMethod.Patch,
            $"annotations({_annotationId})",
            payload,
            new Dictionary<string, string> { ["If-Match"] = "*" },
            ct: ct
        ).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes an annotation from Dataverse.
    /// </summary>
    /// <param name="annotationId">The ID of the annotation to delete</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Task representing the asynchronous operation</returns>
    public async Task DeleteAsync(Guid annotationId, CancellationToken ct = default)
    {
        await _client.ExecuteRequestAsync(
            HttpMethod.Delete,
            $"annotations({annotationId})",
            ct: ct
        ).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the annotation specified by WithId().
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Task representing the asynchronous operation</returns>
    /// <exception cref="InvalidOperationException">Thrown when annotation ID is not set</exception>
    public async Task DeleteAsync(CancellationToken ct = default)
    {
        _guard.Use();

        if (_annotationId == null)
        {
            throw new InvalidOperationException("AnnotationId must be set for delete operations. Use WithId() method.");
        }

        await DeleteAsync(_annotationId.Value, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves an annotation from Dataverse.
    /// </summary>
    /// <param name="annotationId">The ID of the annotation to retrieve</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The annotation record</returns>
    public async Task<Annotation> RetrieveAsync(Guid annotationId, CancellationToken ct = default)
    {
        var response = await _client.ExecuteRequestAsync(
            HttpMethod.Get,
            $"annotations({annotationId})?$select=annotationid,subject,notetext,filename,mimetype,documentbody,filesize,isdocument,createdon,modifiedon,_objectid_value,objecttypecode",
            ct: ct
        ).ConfigureAwait(false);

        return JsonSerializer.Deserialize<Annotation>(response.ToJsonString()) 
               ?? new Annotation();
    }

    /// <summary>
    /// Retrieves the annotation specified by WithId().
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The annotation record</returns>
    /// <exception cref="InvalidOperationException">Thrown when annotation ID is not set</exception>
    public async Task<Annotation> RetrieveAsync(CancellationToken ct = default)
    {
        _guard.Use();

        if (_annotationId == null)
        {
            throw new InvalidOperationException("AnnotationId must be set for retrieve operations. Use WithId() method.");
        }

        return await RetrieveAsync(_annotationId.Value, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads the file content from an annotation.
    /// </summary>
    /// <param name="annotationId">The ID of the annotation</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A tuple containing the file name, MIME type, and file content</returns>
    public async Task<(string FileName, string MimeType, byte[] Content)> DownloadFileAsync(Guid annotationId, CancellationToken ct = default)
    {
        var response = await _client.ExecuteRequestAsync(
            HttpMethod.Get,
            $"annotations({annotationId})?$select=filename,mimetype,documentbody",
            ct: ct
        ).ConfigureAwait(false);

        var fileName = response["filename"]?.GetValue<string>() ?? "unknown";
        var mimeType = response["mimetype"]?.GetValue<string>() ?? "application/octet-stream";
        var base64Content = response["documentbody"]?.GetValue<string>() ?? string.Empty;
        var content = Convert.FromBase64String(base64Content);

        return (fileName, mimeType, content);
    }

    /// <summary>
    /// Downloads the file content from the annotation specified by WithId().
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A tuple containing the file name, MIME type, and file content</returns>
    /// <exception cref="InvalidOperationException">Thrown when annotation ID is not set</exception>
    public async Task<(string FileName, string MimeType, byte[] Content)> DownloadFileAsync(CancellationToken ct = default)
    {
        _guard.Use();

        if (_annotationId == null)
        {
            throw new InvalidOperationException("AnnotationId must be set for download operations. Use WithId() method.");
        }

        return await DownloadFileAsync(_annotationId.Value, ct).ConfigureAwait(false);
    }

    private string BuildRegardingAlternateKeyUrl()
    {
        if (_regardingAlternateKey == null || _regardingAlternateKey.Count == 0)
            throw new InvalidOperationException("Alternate key must be specified.");

        return $"{_regardingEntitySet}({AlternateKeyFormatter.FormatCompositeKey(_regardingAlternateKey)})";
    }

    private static string GetJsonPropertyName<T, TProperty>(Expression<Func<T, TProperty>> propertyExpression)
    {
        MemberExpression? memberExpression = null;

        // Handle direct property access: x => x.Property
        if (propertyExpression.Body is MemberExpression directMember)
        {
            memberExpression = directMember;
        }
        // Handle converted expressions (e.g., x => (object)x.Property or nullable conversions)
        else if (propertyExpression.Body is UnaryExpression unaryExpression
                 && unaryExpression.Operand is MemberExpression convertedMember)
        {
            memberExpression = convertedMember;
        }

        if (memberExpression == null)
        {
            throw new ArgumentException(
                "Expression must be a simple property accessor (e.g., x => x.PropertyName)",
                nameof(propertyExpression));
        }

        if (memberExpression.Member is not PropertyInfo)
        {
            throw new ArgumentException(
                "Expression must reference a property",
                nameof(propertyExpression));
        }

        return ODataPropertyNameCache.GetODataPropertyName(memberExpression.Member);
    }

    private static string InferMimeType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".html" or ".htm" => "text/html",
            ".xml" => "application/xml",
            ".json" => "application/json",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed",
            ".7z" => "application/x-7z-compressed",
            ".mp3" => "audio/mpeg",
            ".mp4" => "video/mp4",
            ".avi" => "video/x-msvideo",
            _ => "application/octet-stream"
        };
    }

    // The table's logical name names the regarding navigation property (objectid_{logicalname}).
    private static string InferEntityLogicalName<T>()
        => DataverseTypeMapping.ResolveEntityLogicalName(typeof(T)) ?? typeof(T).Name.ToLowerInvariant();

    // Dataverse's default entity set name is the English plural of the logical name.
    private static string Pluralize(string logicalName)
    {
        if (logicalName.Length > 1 && logicalName.EndsWith('y') && !"aeiou".Contains(logicalName[^2]))
            return logicalName[..^1] + "ies";

        if (logicalName.EndsWith('s') || logicalName.EndsWith('x') || logicalName.EndsWith('z')
            || logicalName.EndsWith("ch", StringComparison.Ordinal) || logicalName.EndsWith("sh", StringComparison.Ordinal))
            return logicalName + "es";

        return logicalName + "s";
    }
}
