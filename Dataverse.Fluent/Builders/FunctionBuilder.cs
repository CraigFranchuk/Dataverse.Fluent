using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Implementation of fluent builder for executing Dataverse functions.
/// Supports both bound and unbound functions with query string parameters.
/// </summary>
/// <typeparam name="TResponse">The response POCO type for the function result.</typeparam>
/// <remarks>
/// This type is not thread-safe. Each instance should be used for a single operation and not shared across threads.
/// Terminal methods (e.g., <see cref="ExecuteAsync"/>) mark the builder as consumed; subsequent calls will throw
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class FunctionBuilder<TResponse>
{
    private readonly IDataverseTransport _client;
    private readonly string _functionName;
    private readonly string? _entitySet;
    private readonly Guid? _entityId;
    private readonly Dictionary<string, object?> _parameters = [];
    private SingleUseGuard _guard;

    internal FunctionBuilder(IDataverseTransport client, string functionName, string? entitySet = null, Guid? entityId = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _functionName = functionName ?? throw new ArgumentNullException(nameof(functionName));
        _entitySet = entitySet;
        _entityId = entityId;
    }

    /// <summary>
    /// Adds a parameter to the function call (appended as query string).
    /// </summary>
    /// <param name="parameterName">The parameter name</param>
    /// <param name="value">The parameter value</param>
    public FunctionBuilder<TResponse> WithParameter(string parameterName, object? value)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
            throw new ArgumentNullException(nameof(parameterName));

        _parameters[parameterName] = value;
        return this;
    }

    /// <summary>
    /// Executes the function and returns the response.
    /// </summary>
    /// <param name="ct">Optional cancellation token</param>
    public async Task<TResponse?> ExecuteAsync(CancellationToken ct = default)
    {
        _guard.Use();
        var url = BuildFunctionUrl();
        var resultJson = await RequestSender.ExecuteJsonAsync(_client, HttpMethod.Get, url, null, null, ct).ConfigureAwait(false);

        if (typeof(TResponse) == typeof(object))
        {
            return default;
        }

        return DataverseClient.DeserializeEntity<TResponse>(resultJson);
    }

    // Parameters are passed as OData parameter aliases: Func(Name=@p1,Amount=@p2)?@p1='a b'&@p2=1.5.
    // Aliases keep the function segment free of literals, so each value is escaped once as a
    // query-string value and complex values can be sent as JSON (e.g. {"@odata.id":"accounts(...)"}).
    private string BuildFunctionUrl()
    {
        var baseUrl = _entitySet != null && _entityId.HasValue
            ? $"{_entitySet}({_entityId.Value})/Microsoft.Dynamics.CRM.{_functionName}"
            : _functionName;

        if (_parameters.Count == 0)
        {
            return baseUrl;
        }

        var assignments = new List<string>(_parameters.Count);
        var aliasValues = new List<string>(_parameters.Count);
        var index = 1;
        foreach (var (name, value) in _parameters)
        {
            var alias = $"@p{index++}";
            assignments.Add($"{name}={alias}");
            aliasValues.Add($"{alias}={Uri.EscapeDataString(FormatLiteral(value))}");
        }

        return $"{baseUrl}({string.Join(",", assignments)})?{string.Join("&", aliasValues)}";
    }

    // Formats a parameter value as an OData literal (before URL encoding). Checked against Dataverse on 2026-11-18:
    // GUIDs and dates must be unquoted ("Edm.String cannot be converted to Edm.Guid"), and an enum must be the
    // qualified member, Microsoft.Dynamics.CRM.Type'Member' ("Edm.Int32 cannot be converted to ...").
    internal static string FormatLiteral(object? value) => value switch
    {
        null => "null",
        string s => ODataLiteral.Quote(s),
        Guid g => g.ToString(),
        bool b => b ? "true" : "false",
        // Converted to UTC before the 'Z' is appended (see ODataLiteral).
        DateTime dt => ODataLiteral.FormatDateTime(dt),
        DateTimeOffset dto => ODataLiteral.FormatDateTime(dto),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Enum e => FormatEnum(e),
        sbyte or byte or short or ushort or int or uint or long or ulong or decimal or double or float
            => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        _ => JsonSerializer.Serialize(value, value.GetType(), JsonDefaults.PayloadSerialization)
    };

    // Microsoft.Dynamics.CRM.EntityFilters'Entity,Attributes': the C# enum's type and member names must match the
    // Dataverse enum's, as the library has no metadata to map them.
    private static string FormatEnum(Enum value)
        => $"Microsoft.Dynamics.CRM.{value.GetType().Name}'{EnumMemberNames.Format(value).Replace(", ", ",", StringComparison.Ordinal)}'";
}
