using Microsoft.Identity.Client;

namespace Dataverse.Fluent.IntegrationTests;

/// <summary>
/// The authenticated client shared by every integration test in the collection. Every record the tests create
/// through it is tracked and deleted when the run ends (see <see cref="RecordTrackingTransport"/>).
/// </summary>
public class DataverseTestFixture : IAsyncLifetime
{
    // Dataverse accepts up to 1,000 operations in a batch.
    private const int DeleteBatchSize = 500;

    private DataverseClient? _client;
    private DataverseHttpTransport? _http;
    private RecordTrackingTransport? _tracking;
    private readonly DataverseTestConfiguration _config;
    private IConfidentialClientApplication? _confidentialApp;
    private IPublicClientApplication? _publicApp;

    public DataverseClient Client => _client ?? throw new InvalidOperationException("Test fixture not initialized.");

    public DataverseTestConfiguration Configuration => _config;

    public DataverseTestFixture()
    {
        _config = DataverseTestConfiguration.Load();
    }

    public async ValueTask InitializeAsync()
    {
        _config.Validate();

        Func<Task<string>> tokenProvider = _config.AuthMode switch
        {
            AuthenticationMode.ClientCredentials => GetClientCredentialsTokenAsync,
            AuthenticationMode.InteractiveBrowser => GetInteractiveTokenAsync,
            AuthenticationMode.DefaultAzureCredential => GetDefaultAzureCredentialTokenAsync,
            _ => throw new NotSupportedException($"Authentication mode {_config.AuthMode} is not supported.")
        };

        _http = new DataverseHttpTransport(new DataverseClientOptions { BaseUrl = _config.BaseUrl, TokenProvider = tokenProvider });
        _tracking = new RecordTrackingTransport(_http);
        _client = new DataverseClient(_tracking);

        await Task.CompletedTask;
    }

    private async Task<string> GetClientCredentialsTokenAsync()
    {
        if (_confidentialApp == null)
        {
            _confidentialApp = ConfidentialClientApplicationBuilder.Create(_config.ClientId)
                .WithClientSecret(_config.ClientSecret)
                .WithAuthority(new Uri($"https://login.microsoftonline.com/{_config.TenantId}"))
                .Build();
        }

        var result = await _confidentialApp.AcquireTokenForClient(
            new[] { $"{_config.EnvironmentUrl}/.default" }
        ).ExecuteAsync();

        return result.AccessToken;
    }

    private async Task<string> GetInteractiveTokenAsync()
    {
        if (_publicApp == null)
        {
            _publicApp = PublicClientApplicationBuilder.Create(_config.ClientId)
                .WithAuthority(new Uri($"https://login.microsoftonline.com/{_config.TenantId}"))
                .WithRedirectUri("http://localhost")
                .Build();
        }

        try
        {
            var accounts = await _publicApp.GetAccountsAsync();
            var result = await _publicApp.AcquireTokenSilent(
                new[] { $"{_config.EnvironmentUrl}/.default" },
                accounts.FirstOrDefault()
            ).ExecuteAsync();

            return result.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            var result = await _publicApp.AcquireTokenInteractive(
                new[] { $"{_config.EnvironmentUrl}/.default" }
            ).ExecuteAsync();

            return result.AccessToken;
        }
    }

    private async Task<string> GetDefaultAzureCredentialTokenAsync()
    {
        throw new NotImplementedException("DefaultAzureCredential is not yet implemented. Use ClientCredentials or InteractiveBrowser.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_http != null && _tracking != null)
                await DeleteCreatedRecordsAsync(_http, _tracking.Created);
        }
        finally
        {
            _client?.Dispose();
            _http?.Dispose();
        }
    }

    // Deletes newest first, so children go before their parents. Each delete is its own changeset with
    // continue-on-error: a record already deleted by its test, or by a cascade from its parent, answers 404 without
    // stopping the rest.
    private static async Task DeleteCreatedRecordsAsync(IDataverseTransport transport, IReadOnlyList<(string EntitySet, Guid Id)> created)
    {
        var client = new DataverseClient(transport);
        var newestFirst = created.Reverse().ToList();
        var failed = new List<string>();

        foreach (var chunk in newestFirst.Chunk(DeleteBatchSize))
        {
            var batch = client.Batch().ContinueOnError();
            foreach (var (entitySet, id) in chunk)
                batch.AddDelete(entitySet, id).NewChangeset();

            try
            {
                var result = await batch.ExecuteAsync();
                failed.AddRange(result.Results
                    .Select((r, i) => (Result: r, Record: chunk[i]))
                    .Where(x => !x.Result.IsSuccess && x.Result.StatusCode != System.Net.HttpStatusCode.NotFound)
                    .Select(x => $"{x.Record.EntitySet}({x.Record.Id}): {(int)x.Result.StatusCode}"));
            }
            catch (Exception ex)
            {
                failed.Add($"batch of {chunk.Length} deletes: {ex.Message}");
            }
        }

        Console.WriteLine($"Integration test cleanup: {created.Count} records created, {failed.Count} could not be deleted.");
        foreach (var failure in failed)
            Console.WriteLine($"  not deleted: {failure}");
    }
}
