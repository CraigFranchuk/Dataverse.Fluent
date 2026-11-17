# Dataverse.Fluent

A fluent, strongly typed C# client for the Microsoft Dataverse Web API, with no metadata dependency.

| Package | Description |
|---------|-------------|
| `Dataverse.Fluent` | The client. See the [package README](Dataverse.Fluent/README.md) and the [guides](Dataverse.Fluent/docs/README.md). |
| `Dataverse.Fluent.Sales` | Mapped Dynamics 365 Sales tables and typed Sales actions. See its [README](Dataverse.Fluent.Sales/README.md). |

```csharp
var client = new DataverseClient("https://yourorg.api.crm.dynamics.com/api/data/v9.2", new DefaultAzureCredential());

var accounts = await client.Query<Account>()
    .Where(a => a.Revenue > 1_000_000)
    .OrderBy(a => a.Name)
    .GetListAsync();
```

## Repository

| Project | Purpose |
|---------|---------|
| `Dataverse.Fluent` | The library |
| `Dataverse.Fluent.Tests` | Unit tests |
| `Dataverse.Fluent.IntegrationTests` | Tests against a live Dataverse environment ([README](Dataverse.Fluent.IntegrationTests/README.md)) |
| `Dataverse.Fluent.Sales` and its test projects | The Dynamics 365 Sales package |
| `Dataverse.Fluent.Benchmarks` | BenchmarkDotNet benchmarks |
| `Examples` | Usage examples, compiled with the solution |

See [CONTRIBUTING.md](CONTRIBUTING.md) to build and test, and [CHANGELOG.md](CHANGELOG.md) for releases.

Licensed under the [MIT License](LICENSE).
