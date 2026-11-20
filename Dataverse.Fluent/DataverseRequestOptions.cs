using System.Globalization;

namespace Dataverse.Fluent;

/// <summary>
/// Optional Dataverse behaviours sent as request headers: bypassing custom business logic and Power Automate flows,
/// impersonating another user, duplicate detection and solution association. Apply them to every request a client
/// sends with <see cref="DataverseClientExtensions.WithOptions(IDataverseClient, Action{DataverseRequestOptions})"/>.
/// </summary>
/// <remarks>
/// The <c>tag</c> and <c>partitionid</c> optional parameters are query string options, not headers, so they are not
/// covered here.
/// </remarks>
/// <example>
/// <code>
/// // A data load that skips custom plug-ins, workflows and flows.
/// var loader = client.WithOptions(o =>
/// {
///     o.BypassBusinessLogic = BusinessLogicBypass.CustomSync | BusinessLogicBypass.CustomAsync;
///     o.BypassPowerAutomateFlows = true;
/// });
///
/// // Act on behalf of another user.
/// var asUser = client.WithOptions(o => o.CallerObjectId = userObjectId);
/// </code>
/// </example>
public sealed class DataverseRequestOptions
{
    internal const string BypassBusinessLogicHeader = "MSCRM.BypassBusinessLogicExecution";
    internal const string BypassStepIdsHeader = "MSCRM.BypassBusinessLogicExecutionStepIds";
    internal const string BypassFlowsHeader = "MSCRM.SuppressCallbackRegistrationExpanderJob";
    internal const string SuppressDuplicateDetectionHeader = "MSCRM.SuppressDuplicateDetection";
    internal const string SolutionUniqueNameHeader = "MSCRM.SolutionUniqueName";
    internal const string CallerObjectIdHeader = "CallerObjectId";
    internal const string CallerSystemUserIdHeader = "MSCRMCallerID";

    /// <summary>
    /// The custom business logic to bypass (<c>MSCRM.BypassBusinessLogicExecution</c>): synchronous plug-ins and
    /// workflows, asynchronous ones, or both. Logic in solutions published by Microsoft still runs, and so do Power
    /// Automate flows (see <see cref="BypassPowerAutomateFlows"/>). The calling user needs the
    /// <c>prvBypassCustomBusinessLogic</c> privilege.
    /// </summary>
    public BusinessLogicBypass BypassBusinessLogic { get; set; }

    /// <summary>
    /// The IDs of specific plug-in step registrations to bypass (<c>MSCRM.BypassBusinessLogicExecutionStepIds</c>),
    /// instead of all custom logic. Dataverse accepts three steps by default. The calling user needs the
    /// <c>prvBypassCustomBusinessLogic</c> privilege.
    /// </summary>
    public IList<Guid> BypassPluginStepIds { get; } = [];

    /// <summary>
    /// Whether to skip triggering Power Automate flows (<c>MSCRM.SuppressCallbackRegistrationExpanderJob</c>).
    /// Flow owners are not notified that their flows did not run.
    /// </summary>
    public bool BypassPowerAutomateFlows { get; set; }

    /// <summary>
    /// Whether a create or update that matches a published duplicate detection rule fails with HTTP 412
    /// (<c>MSCRM.SuppressDuplicateDetection: false</c>). The Web API does not detect duplicates by default.
    /// </summary>
    public bool DetectDuplicates { get; set; }

    /// <summary>
    /// The unique name of an unmanaged solution to add created or updated solution components to
    /// (<c>MSCRM.SolutionUniqueName</c>).
    /// </summary>
    public string? SolutionUniqueName { get; set; }

    /// <summary>
    /// Impersonates the user with this Microsoft Entra object ID (<c>CallerObjectId</c>), the preferred way to
    /// impersonate. The calling user needs the <c>prvActOnBehalfOfAnotherUser</c> privilege. Set either this or
    /// <see cref="CallerSystemUserId"/>.
    /// </summary>
    public Guid? CallerObjectId { get; set; }

    /// <summary>
    /// Impersonates the user with this <c>systemuserid</c> (<c>MSCRMCallerID</c>, the legacy header). Set either this
    /// or <see cref="CallerObjectId"/>.
    /// </summary>
    public Guid? CallerSystemUserId { get; set; }

    /// <summary>
    /// Other headers to send with every request, such as one Dataverse adds after this release. Names are
    /// case-insensitive. A header that a property of these options also sets is rejected.
    /// </summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Validates the options and returns the headers they send. The result is a snapshot: changing the options
    /// afterwards does not change it.
    /// </summary>
    /// <exception cref="ArgumentException">The options are invalid or contradictory.</exception>
    internal Dictionary<string, string> BuildHeaders()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (BypassBusinessLogic != BusinessLogicBypass.None)
            headers[BypassBusinessLogicHeader] = FormatBypass(BypassBusinessLogic);

        if (BypassPluginStepIds.Count > 0)
        {
            if (BypassPluginStepIds.Contains(Guid.Empty))
                throw new ArgumentException($"{nameof(BypassPluginStepIds)} contains an empty GUID.");
            headers[BypassStepIdsHeader] = string.Join(",", BypassPluginStepIds.Distinct().Select(FormatGuid));
        }

        if (BypassPowerAutomateFlows)
            headers[BypassFlowsHeader] = "true";

        if (DetectDuplicates)
            headers[SuppressDuplicateDetectionHeader] = "false";

        if (SolutionUniqueName != null)
        {
            if (string.IsNullOrWhiteSpace(SolutionUniqueName))
                throw new ArgumentException($"{nameof(SolutionUniqueName)} must not be empty.");
            headers[SolutionUniqueNameHeader] = ValidateValue(SolutionUniqueNameHeader, SolutionUniqueName);
        }

        if (CallerObjectId != null && CallerSystemUserId != null)
            throw new ArgumentException($"Set either {nameof(CallerObjectId)} or {nameof(CallerSystemUserId)}, not both.");
        if (CallerObjectId is { } objectId)
            headers[CallerObjectIdHeader] = FormatCaller(objectId, nameof(CallerObjectId));
        if (CallerSystemUserId is { } systemUserId)
            headers[CallerSystemUserIdHeader] = FormatCaller(systemUserId, nameof(CallerSystemUserId));

        foreach (var (name, value) in Headers)
        {
            ValidateName(name);
            if (headers.ContainsKey(name))
                throw new ArgumentException($"The '{name}' header is set by a property of {nameof(DataverseRequestOptions)}; set it there, not in {nameof(Headers)}.");
            headers[name] = ValidateValue(name, value);
        }

        return headers;
    }

    private static string FormatBypass(BusinessLogicBypass bypass)
    {
        const BusinessLogicBypass known = BusinessLogicBypass.CustomSync | BusinessLogicBypass.CustomAsync;
        if ((bypass & ~known) != 0)
            throw new ArgumentOutOfRangeException(nameof(bypass), bypass, $"{nameof(BypassBusinessLogic)} has an unknown value.");

        return bypass switch
        {
            BusinessLogicBypass.CustomSync => "CustomSync",
            BusinessLogicBypass.CustomAsync => "CustomAsync",
            _ => "CustomSync,CustomAsync"
        };
    }

    private static string FormatCaller(Guid id, string property)
        => id == Guid.Empty
            ? throw new ArgumentException($"{property} must not be an empty GUID.")
            : FormatGuid(id);

    private static string FormatGuid(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    // Header names are HTTP tokens (RFC 9110). Validated here because batch parts are written as raw text, where a
    // separator or line break in a name would corrupt the multipart body.
    private static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || !name.All(IsTokenChar))
            throw new ArgumentException($"'{name}' is not a valid header name.");
    }

    private static bool IsTokenChar(char c) => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c, StringComparison.Ordinal);

    private static string ValidateValue(string name, string? value)
    {
        if (value == null || value.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0)
            throw new ArgumentException($"The value of the '{name}' header must not be null or contain line breaks.");
        return value;
    }
}

/// <summary>
/// The custom business logic to bypass with <see cref="DataverseRequestOptions.BypassBusinessLogic"/>. Combine the
/// values to bypass both.
/// </summary>
[Flags]
public enum BusinessLogicBypass
{
    /// <summary>Run all business logic.</summary>
    None = 0,

    /// <summary>Bypass custom synchronous plug-ins and workflows (<c>CustomSync</c>).</summary>
    CustomSync = 1,

    /// <summary>Bypass custom asynchronous plug-ins and workflows, but not Power Automate flows (<c>CustomAsync</c>).</summary>
    CustomAsync = 2
}
