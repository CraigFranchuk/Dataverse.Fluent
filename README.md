# Dataverse.Fluent

A fluent, strongly typed C# client for the Microsoft Dataverse Web API, with no metadata dependency.

| Package | Description |
|---------|-------------|
| `Dataverse.Fluent` | The client. See the [package README](Dataverse.Fluent/README.md) and the [guides](Dataverse.Fluent/docs/README.md). |
| `Dataverse.Fluent.Sales` | Mapped Dynamics 365 Sales tables and typed Sales actions. See its [README](Dataverse.Fluent.Sales/README.md). |
| `Dataverse.Fluent.Testing` | A fake transport for unit testing code that uses the client. See its [README](Dataverse.Fluent.Testing/README.md). |
| `Dataverse.Fluent.Schema` | Checks models against an environment's metadata, in a test. See its [README](Dataverse.Fluent.Schema/README.md). |
| `Dataverse.Fluent.Tool` | The `dataverse-fluent` command: validate a project's models and fix them. See its [README](Dataverse.Fluent.Tool/README.md). |

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
| `Dataverse.Fluent.Testing` and `Dataverse.Fluent.Testing.Tests` | The fake transport package |
| `Dataverse.Fluent.Schema` and `Dataverse.Fluent.Schema.Tests` | The schema validator package |
| `Dataverse.Fluent.Tool` and `Dataverse.Fluent.Tool.Tests` | The `dataverse-fluent` dotnet tool |
| `Dataverse.Fluent.Benchmarks` | BenchmarkDotNet benchmarks |
| `Examples` | Usage examples, compiled with the solution |

See [CONTRIBUTING.md](CONTRIBUTING.md) to build and test, and [CHANGELOG.md](CHANGELOG.md) for releases.

Licensed under the [MIT License](LICENSE).
