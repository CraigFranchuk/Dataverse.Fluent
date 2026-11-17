using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Models;

/// <summary>
/// Represents a Dataverse Annotation (Note) entity with file attachment support.
/// </summary>
[EntitySet("annotations")]
public class Annotation
{
    /// <summary>The note's primary key (<c>annotationid</c>).</summary>
    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("annotationid")]
    public Guid AnnotationId { get; set; }

    /// <summary>The note's title (<c>subject</c>).</summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    /// <summary>The note's body text (<c>notetext</c>).</summary>
    [JsonPropertyName("notetext")]
    public string? NoteText { get; set; }

    /// <summary>The attachment's file name (<c>filename</c>).</summary>
    [JsonPropertyName("filename")]
    public string? FileName { get; set; }

    /// <summary>The attachment's MIME type (<c>mimetype</c>).</summary>
    [JsonPropertyName("mimetype")]
    public string? MimeType { get; set; }

    /// <summary>
    /// Base64-encoded file content. This property is used for both upload and download.
    /// </summary>
    [JsonPropertyName("documentbody")]
    public string? DocumentBody { get; set; }

    /// <summary>The attachment's size in bytes (<c>filesize</c>). Set by Dataverse.</summary>
    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("filesize")]
    public int? FileSize { get; set; }

    /// <summary>Whether the note carries an attachment (<c>isdocument</c>).</summary>
    [JsonPropertyName("isdocument")]
    public bool? IsDocument { get; set; }

    /// <summary>When the note was created (<c>createdon</c>). Set by Dataverse.</summary>
    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("createdon")]
    public DateTime CreatedOn { get; set; }

    /// <summary>When the note was last modified (<c>modifiedon</c>). Set by Dataverse.</summary>
    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("modifiedon")]
    public DateTime ModifiedOn { get; set; }

    /// <summary>The ID of the record the note is attached to (<c>_objectid_value</c>).</summary>
    [JsonPropertyName("_objectid_value")]
    public Guid? ObjectId { get; set; }

    /// <summary>The logical name of the table the note is attached to (<c>objecttypecode</c>).</summary>
    [JsonPropertyName("objecttypecode")]
    public string? ObjectTypeCode { get; set; }
}
