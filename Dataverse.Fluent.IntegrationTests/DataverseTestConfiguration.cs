using Microsoft.Extensions.Configuration;

namespace Dataverse.Fluent.IntegrationTests;

public class DataverseTestConfiguration
{
    public string EnvironmentUrl { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v9.2";
    public AuthenticationMode AuthMode { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>
    /// A text column on Account with a single-column alternate key. Account has none out of the box, so the
    /// alternate-key tests are skipped when this is not set.
    /// </summary>
    public string? AccountAlternateKeyColumn { get; set; }

    public string BaseUrl => $"{EnvironmentUrl.TrimEnd('/')}/api/data/{ApiVersion}";

    public static DataverseTestConfiguration Load()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.local.json", optional: true)
            .AddUserSecrets<DataverseTestConfiguration>(optional: true)
            .AddEnvironmentVariables(prefix: "DATAVERSE_")
            .Build();

        var config = new DataverseTestConfiguration();
        configuration.GetSection("Dataverse").Bind(config);

        return config;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(EnvironmentUrl))
            throw new InvalidOperationException("Dataverse EnvironmentUrl is not configured.");

        if (AuthMode == AuthenticationMode.ClientCredentials)
        {
            if (string.IsNullOrWhiteSpace(TenantId))
                throw new InvalidOperationException("TenantId is required for ClientCredentials authentication.");
            if (string.IsNullOrWhiteSpace(ClientId))
                throw new InvalidOperationException("ClientId is required for ClientCredentials authentication.");
            if (string.IsNullOrWhiteSpace(ClientSecret))
                throw new InvalidOperationException("ClientSecret is required for ClientCredentials authentication.");
        }
        else if (AuthMode == AuthenticationMode.InteractiveBrowser)
        {
            if (string.IsNullOrWhiteSpace(TenantId))
                throw new InvalidOperationException("TenantId is required for InteractiveBrowser authentication.");
            if (string.IsNullOrWhiteSpace(ClientId))
                throw new InvalidOperationException("ClientId is required for InteractiveBrowser authentication.");
        }
    }
}

public enum AuthenticationMode
{
    ClientCredentials,
    InteractiveBrowser,
    DefaultAzureCredential
}
