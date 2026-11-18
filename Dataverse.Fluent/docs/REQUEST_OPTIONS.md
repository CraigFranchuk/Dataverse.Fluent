# Request Options

Dataverse changes how a request is processed when it carries certain headers. These headers can bypass custom
business logic, impersonate another user, turn on duplicate detection, or add solution components to a solution.
`WithOptions` returns a client that sends them with every request:

```csharp
var loader = client.WithOptions(o =>
{
    o.BypassBusinessLogic = BusinessLogicBypass.CustomSync | BusinessLogicBypass.CustomAsync;
    o.BypassPowerAutomateFlows = true;
});

await loader.Batch()
    .AddCreate(new Account { Name = "Contoso" })
    .AddCreate(new Account { Name = "Fabrikam" })
    .ExecuteAsync();
```

The original client is not changed. The returned client shares its transport, so it is cheap to create and needs no
disposal. Do not use it after the original client is disposed.

## Options

| Property | Header | Effect |
|----------|--------|--------|
| `BypassBusinessLogic` | `MSCRM.BypassBusinessLogicExecution` | Skips custom synchronous (`CustomSync`) and/or asynchronous (`CustomAsync`) plug-ins and workflows. Requires `prvBypassCustomBusinessLogic`. |
| `BypassPluginStepIds` | `MSCRM.BypassBusinessLogicExecutionStepIds` | Skips only the listed plug-in steps (three by default). Requires `prvBypassCustomBusinessLogic`. |
| `BypassPowerAutomateFlows` | `MSCRM.SuppressCallbackRegistrationExpanderJob: true` | Skips triggering Power Automate flows. Flow owners are not notified. |
| `DetectDuplicates` | `MSCRM.SuppressDuplicateDetection: false` | Makes a create or update that matches a duplicate detection rule fail with HTTP 412. The Web API does not detect duplicates by default. |
| `SolutionUniqueName` | `MSCRM.SolutionUniqueName` | Adds created or updated solution components to this unmanaged solution. |
| `CallerObjectId` | `CallerObjectId` | Impersonates the user with this Microsoft Entra object ID. Requires `prvActOnBehalfOfAnotherUser`. |
| `CallerSystemUserId` | `MSCRMCallerID` | Impersonates the user with this `systemuserid`. This is the legacy header; prefer `CallerObjectId`. |
| `Headers` | any | Other headers, such as one added to Dataverse after this release. |

Bypassing business logic never skips logic in solutions published by Microsoft, and it does not stop Power Automate
flows. See Microsoft's [Bypass custom Dataverse logic](https://learn.microsoft.com/power-apps/developer/data-platform/bypass-custom-business-logic)
and [Impersonate another user](https://learn.microsoft.com/power-apps/developer/data-platform/impersonate-another-user).

The `tag` and `partitionid` optional parameters go in the query string, not a header, so these options do not cover
them.

## Impersonation

```csharp
var asUser = client.WithOptions(o => o.CallerObjectId = userObjectId);

var created = await asUser.Entity<Account>().Set(a => a.Name, "Created on behalf of a user").CreateAsync();
```

The record's `createdby` is the impersonated user, and `createdonbehalfby` is the calling user. Reads run with the
impersonated user's security as well, so they return only the records that user can see.

## Batches

Dataverse does not apply the headers of a `$batch` request to the operations inside it. A batch built from a client
returned by `WithOptions` therefore carries the headers in every operation, and on the batch request itself.

A header that an operation sets itself takes precedence over the same header from the options:

```csharp
var request = loader.Entity<WebResource>().SetFrom(resource).ToCreateRequest();
request.Headers["MSCRM.SolutionUniqueName"] = "OtherSolution"; // only this operation

await loader.Batch().Add(request).ExecuteAsync();
```

## Combining options

Calling `WithOptions` on a client returned by `WithOptions` adds to its options. When both calls set an option, the
newer value wins. Setting either impersonation ID replaces the earlier impersonated user.

```csharp
var importer = client.WithOptions(o => o.BypassBusinessLogic = BusinessLogicBypass.CustomSync);
var importAsUser = importer.WithOptions(o => o.CallerObjectId = userObjectId); // bypass and impersonate
```

The options are read once, so changing a `DataverseRequestOptions` instance after the call does not affect the
client. The options are validated when `WithOptions` is called. It throws `ArgumentException` in these cases:

- Both impersonation IDs are set.
- An ID is `Guid.Empty`.
- A header name is not a valid HTTP token, or a value contains a line break.
- A header in `Headers` is one that a property already sets.

## Dependency injection

A registered `IDataverseClient` can be scoped where it is used:

```csharp
public class AccountImporter(IDataverseClient dataverse)
{
    private readonly IDataverseClient _loader =
        dataverse.WithOptions(o => o.BypassBusinessLogic = BusinessLogicBypass.CustomSync);

    public async Task ImportAsync(IEnumerable<Account> accounts)
    {
        var batch = _loader.Batch();
        foreach (var account in accounts)
            batch.AddCreate(account);

        (await batch.ExecuteAsync()).ThrowIfAnyFailed();
    }
}
```
