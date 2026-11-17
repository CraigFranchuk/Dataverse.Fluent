# Getting Started

This guide takes you from an empty console project to querying and writing Dataverse records.

You need the .NET 10 SDK or later, a Dataverse environment, and permission to register an application in Microsoft
Entra ID (or an existing registration).

## 1. Create a project

```bash
dotnet new console -n MyDataverseApp
cd MyDataverseApp
dotnet add package Dataverse.Fluent
dotnet add package Azure.Identity
```

## 2. Give your application access

Dataverse accepts OAuth tokens from Microsoft Entra ID. How you obtain one depends on who the application acts as.

### As itself (service-to-service)

For services, scheduled jobs and integrations:

1. In the Azure portal, open **Microsoft Entra ID** > **App registrations** > **New registration** and register an
   application. Note the **Application (client) ID** and **Directory (tenant) ID**.
2. Under **Certificates & secrets**, add a client secret (or a certificate) and copy its value.
3. In the [Power Platform admin center](https://admin.powerplatform.microsoft.com), open the environment, then
   **Settings** > **Users + permissions** > **Application users**, and add a user for the application.
4. Assign that application user a security role with the privileges your code needs.

No API permission has to be granted for this flow: access is controlled by the application user's security roles.
In Azure, a managed identity can take the place of the app registration and secret; add it as an application user the
same way.

### As a signed-in user (delegated)

For desktop tools and web apps that call Dataverse on a user's behalf, register an application, add the
**Dynamics CRM** > **user_impersonation** delegated permission, and acquire tokens interactively or on behalf of the
user (see [Authentication patterns](#authentication-patterns)). The user's own security roles apply.

## 3. Map a table

Map the columns you use; you do not need to model the whole table.

```csharp
using System.Text.Json.Serialization;
using Dataverse.Fluent;

[DataverseEntity("account", "accounts")]          // table logical name and entity set name
public class Account
{
    [DataverseReadOnly(DataverseOperation.Create)] // Dataverse assigns the ID
    public Guid AccountId { get; set; }

    public string? Name { get; set; }              // unmapped names are lowercased: "name"

    public decimal? Revenue { get; set; }

    [JsonPropertyName("industrycode")]
    public int? Industry { get; set; }             // choice columns are integers

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    public DateTime? CreatedOn { get; set; }
}
```

- `[DataverseEntity]` names the table. The entity set name is the one Web API URLs use (`accounts`). It cannot be
  guessed reliably, so the client never pluralizes a type name.
- A property's column is, in order: `[DataverseLookup]`, `[DataverseColumn]` or `[JsonPropertyName]`, otherwise the
  lowercased property name.
- Use nullable types. `null` properties are left out of create and update payloads, so a partly filled object never
  overwrites columns you did not set.
- `[DataverseReadOnly]` leaves system-managed columns out of the operations you name.

## 4. Write a first program

```csharp
using Azure.Identity;
using Dataverse.Fluent;

const string url = "https://yourorg.api.crm.dynamics.com/api/data/v9.2";

// Service-to-service with a client secret. In Azure, ManagedIdentityCredential needs no secret at all.
var credential = new ClientSecretCredential(
    Environment.GetEnvironmentVariable("DATAVERSE_TENANT_ID"),
    Environment.GetEnvironmentVariable("DATAVERSE_CLIENT_ID"),
    Environment.GetEnvironmentVariable("DATAVERSE_CLIENT_SECRET"));

using var client = new DataverseClient(url, credential);

// Query
var large = await client.Query<Account>()
    .Select(a => new { a.Name, a.Revenue })
    .Where(a => a.Revenue > 1_000_000)
    .OrderBy(a => a.Revenue, descending: true)
    .Top(5)
    .GetListAsync();

foreach (var account in large)
    Console.WriteLine($"{account.Name}: {account.Revenue:C0}");

// Create
var created = await client.Entity<Account>()
    .SetFrom(new Account { Name = $"Test {DateTime.Now:yyyy-MM-dd HH:mm:ss}", Revenue = 2_500_000 })
    .CreateAsync();
var id = created.Id!.Value;
Console.WriteLine($"Created {id}");

// Update, returning the updated columns
var updated = await client.Entity<Account>(id)
    .Set(a => a.Revenue, 3_000_000m)
    .ReturnRepresentation(a => new { a.Name, a.Revenue })
    .UpdateAsync();
Console.WriteLine($"{updated!.Name} now has revenue {updated.Revenue:C0}");

// Delete
await client.Entity<Account>(id).DeleteAsync();
```

Set the three `DATAVERSE_*` environment variables and run it with `dotnet run`. Replace the URL with your
environment's Web API URL, which is shown in Power Apps under **Settings** > **Developer resources**.

## 5. Use dependency injection

In an ASP.NET Core or worker application, register the client instead of constructing it:

```csharp
builder.Services.AddDataverseClient(builder.Configuration.GetSection("Dataverse"),
    o => o.Credential = new DefaultAzureCredential());
```

```json
{
  "Dataverse": { "BaseUrl": "https://yourorg.api.crm.dynamics.com/api/data/v9.2" }
}
```

Then inject `IDataverseClient`. The registration uses `IHttpClientFactory`, validates the options at startup, and
applies the same retry, timeout and token-caching pipeline as the constructors. For more than one environment, see
[Dependency injection and multiple environments](../README.md#dependency-injection-and-multiple-environments).

## Authentication patterns

Any `TokenCredential` works, as does a delegate that returns a bearer token. Either way, the client caches the token
until shortly before it expires and refreshes it when Dataverse rejects it.

| Scenario | Credential |
|----------|------------|
| Azure-hosted service | `ManagedIdentityCredential`, or `DefaultAzureCredential` |
| Service with a secret or certificate | `ClientSecretCredential`, `ClientCertificateCredential` |
| Local development | `DefaultAzureCredential` (Visual Studio, Azure CLI) or `InteractiveBrowserCredential` |
| Web API acting for a signed-in user | `OnBehalfOfCredential` |

With MSAL, pass a delegate:

```csharp
var app = ConfidentialClientApplicationBuilder.Create(clientId)
    .WithClientSecret(clientSecret)
    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
    .Build();

using var client = new DataverseClient(url, tokenProvider: async () =>
    (await app.AcquireTokenForClient(["https://yourorg.api.crm.dynamics.com/.default"]).ExecuteAsync()).AccessToken);
```

The scope is the environment's root URL followed by `/.default`.

## Troubleshooting

| Symptom | Check |
|---------|-------|
| `InvalidOperationException`: type does not have an `[EntitySet]` attribute | Add `[DataverseEntity]` or `[EntitySet]` to the class, or pass the entity set name: `Query<Account>("accounts")` |
| 401 Unauthorized | The token's scope is the environment URL plus `/.default`; the secret has not expired; for service-to-service, the application user exists in this environment |
| 403 Forbidden | The user or application user lacks a privilege; the error message names it |
| 404 on a table | The entity set name is wrong (`accounts`, not `account`) |
| 400 "Could not find a property named ..." | A column name is wrong; Dataverse logical names are lowercase |

## Next steps

- [README](../README.md) for queries, batches, FetchXML and the rest of the API
- [Best Practices](BEST_PRACTICES.md) for production guidance
- [Error Handling](ERROR_HANDLING.md) for `DataverseException`
