using Azure.Core;
using Microsoft.Extensions.Options;

namespace Dataverse.Fluent;

/// <summary>
/// Configuration for a <see cref="DataverseClient"/>: the environment URL, how to authenticate, and the
/// resilience pipeline. Used by the options-based constructor and by DI registrations, where it can be
/// bound from configuration.
/// </summary>
/// <example>
/// <code>
/// // appsettings.json
/// // "Dataverse": {
/// //   "Source": { "BaseUrl": "https://source.crm.dynamics.com/api/data/v9.2" },
/// //   "Target": { "BaseUrl": "https://target.crm.dynamics.com/api/data/v9.2",
/// //               "Resilience": { "MaxRetryAttempts": 5 } }
/// // }
/// services.AddKeyedDataverseClient("source", configuration.GetSection("Dataverse:Source"),
///     o => o.Credential = new DefaultAzureCredential());
/// </code>
/// </example>
public sealed class DataverseClientOptions
{
    /// <summary>
    /// The Dataverse Web API URL, e.g. <c>https://yourorg.api.crm.dynamics.com/api/data/v9.2</c>. Must use HTTPS.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// An Azure credential (e.g. <c>DefaultAzureCredential</c> or <c>ManagedIdentityCredential</c>) used to
    /// acquire tokens for <see cref="Scope"/>. Tokens are cached until shortly before their real expiry.
    /// Set either this or <see cref="TokenProvider"/>.
    /// </summary>
    public TokenCredential? Credential { get; set; }

    /// <summary>
    /// A delegate returning a raw bearer token. Tokens are cached until shortly before the JWT <c>exp</c>
    /// claim (or for five minutes when the token is not a JWT). Set either this or <see cref="Credential"/>.
    /// </summary>
    public Func<Task<string>>? TokenProvider { get; set; }

    /// <summary>
    /// The token scope requested from <see cref="Credential"/>. Defaults to <c>https://{host}/.default</c>
    /// derived from <see cref="BaseUrl"/>.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Retry, circuit-breaker and timeout settings. Bound from the <c>Resilience</c> configuration sub-section.
    /// </summary>
    public DataverseResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// Whether request URLs are written to logs and trace tags with their query string. Default:
    /// <c>false</c>, because <c>$filter</c> values (names, e-mail addresses, identifiers) can be personal
    /// data; only the path (e.g. <c>.../accounts</c>) is recorded. Enable it for local diagnostics.
    /// </summary>
    public bool IncludeQueryInTelemetry { get; set; }

    internal string GetScope()
    {
        if (!string.IsNullOrWhiteSpace(Scope))
            return Scope;

        var uri = new Uri(BaseUrl);
        return $"{uri.Scheme}://{uri.Authority}/.default";
    }

    /// <summary>
    /// Returns the validation failures for these options, or an empty list when they are valid.
    /// </summary>
    internal List<string> GetValidationErrors(bool requireAuthentication)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(BaseUrl))
            errors.Add("BaseUrl is required.");
        else if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            errors.Add($"BaseUrl must be an absolute HTTPS URL (Dataverse requires HTTPS). Value: '{BaseUrl}'.");

        if (requireAuthentication)
        {
            if (Credential is null && TokenProvider is null)
                errors.Add("Either Credential or TokenProvider must be set.");
            else if (Credential is not null && TokenProvider is not null)
                errors.Add("Set either Credential or TokenProvider, not both.");
        }

        if (Resilience is null)
        {
            errors.Add("Resilience must not be null.");
        }
        else if (Resilience.Enabled)
        {
            if (Resilience.MaxRetryAttempts < 0)
                errors.Add("Resilience.MaxRetryAttempts must be zero or greater.");
            if (Resilience.AttemptTimeout <= TimeSpan.Zero)
                errors.Add("Resilience.AttemptTimeout must be positive.");
            if (Resilience.TotalRequestTimeout <= Resilience.AttemptTimeout)
                errors.Add("Resilience.TotalRequestTimeout must be greater than Resilience.AttemptTimeout.");
            if (Resilience.MedianFirstRetryDelay < TimeSpan.Zero)
                errors.Add("Resilience.MedianFirstRetryDelay must not be negative.");
        }

        return errors;
    }

    /// <summary>Throws <see cref="ArgumentException"/> when the options are invalid.</summary>
    internal void Validate(bool requireAuthentication)
    {
        var errors = GetValidationErrors(requireAuthentication);
        if (errors.Count > 0)
            throw new ArgumentException("Invalid DataverseClientOptions: " + string.Join(" ", errors));
    }
}

/// <summary>
/// Validates named <see cref="DataverseClientOptions"/> registered through DI, so misconfiguration fails at
/// startup (with <c>ValidateOnStart</c>) or on first use, with a message naming the client.
/// </summary>
internal sealed class DataverseClientOptionsValidator : IValidateOptions<DataverseClientOptions>
{
    private readonly HashSet<string> _registeredNames;

    public DataverseClientOptionsValidator(IEnumerable<DataverseClientRegistration> registrations)
    {
        _registeredNames = registrations.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
    }

    public ValidateOptionsResult Validate(string? name, DataverseClientOptions options)
    {
        name ??= Options.DefaultName;
        if (!_registeredNames.Contains(name))
            return ValidateOptionsResult.Skip;

        var errors = options.GetValidationErrors(requireAuthentication: true);
        var label = name.Length == 0 ? "the default Dataverse client" : $"Dataverse client '{name}'";
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors.Select(e => $"{label}: {e}"));
    }
}
