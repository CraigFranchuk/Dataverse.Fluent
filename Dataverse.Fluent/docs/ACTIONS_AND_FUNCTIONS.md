# Actions and Functions

Dataverse exposes operations beyond create, read, update and delete as *actions* and *functions*.

| | Actions | Functions |
|--|---------|-----------|
| HTTP method | POST | GET |
| Side effects | May change data | Read only |
| Parameters | JSON body | Query string (sent as parameter aliases) |
| Builder | `Action<TRequest, TResponse>(name)` | `Function<TResponse>(name)` |

Either kind can be *unbound* (called on its own) or *bound* to a record (`ForEntity<T>(id)`). Whether an operation is
an action or a function, and whether it is bound, is fixed by Dataverse: check the
[Web API action](https://learn.microsoft.com/power-apps/developer/data-platform/webapi/reference/actions) and
[function](https://learn.microsoft.com/power-apps/developer/data-platform/webapi/reference/functions) references or the
environment's `$metadata`. Calling one the wrong way returns 404.

Request and response types are plain classes. Parameter names are case-sensitive and usually PascalCase, so map
them with `[JsonPropertyName]` or name the properties to match.

## Unbound functions

```csharp
public class WhoAmIResponse
{
    public Guid UserId { get; set; }
    public Guid BusinessUnitId { get; set; }
    public Guid OrganizationId { get; set; }
}

var me = await client.Function<WhoAmIResponse>("WhoAmI").ExecuteAsync();
```

Parameters are added with `WithParameter(name, value)`. Strings, numbers, booleans, GUIDs, dates (in UTC, unquoted) and
`DateOnly` are written as OData literals; any other object, such as a string array, is sent as JSON. An entity
reference is an object with `@odata.id`:

```csharp
// CalculateRollupField(Target=@p1,FieldName=@p2)?@p1={"@odata.id":"accounts(...)"}&@p2='opendeals'
var account = await client.Function<RollupAccount>("CalculateRollupField")
    .WithParameter("Target", new Dictionary<string, string> { ["@odata.id"] = $"accounts({accountId})" })
    .WithParameter("FieldName", "opendeals")
    .ExecuteAsync();
```

An enum parameter is written as its Dataverse enum member, `Microsoft.Dynamics.CRM.{Type}'{Member}'`; Dataverse rejects
the integer value. Declare a C# enum with the same type and member names as the Dataverse enum, and mark it `[Flags]`
when Dataverse's is (a combination is sent as `'Entity,Attributes'`):

```csharp
[Flags]
public enum EntityFilters { Entity = 1, Attributes = 2, Privileges = 4, Relationships = 8 }

// RetrieveEntity(EntityFilters=@p1,...)?@p1=Microsoft.Dynamics.CRM.EntityFilters'Entity,Attributes'
var account = await client.Function<JsonObject>("RetrieveEntity")
    .WithParameter("EntityFilters", EntityFilters.Entity | EntityFilters.Attributes)
    .WithParameter("LogicalName", "account")
    .WithParameter("MetadataId", Guid.Empty)
    .WithParameter("RetrieveAsIfPublished", false)
    .ExecuteAsync();
```

## Bound functions

```csharp
// systemusers(<id>)/Microsoft.Dynamics.CRM.RetrieveUserPrivileges
var privileges = await client
    .ForEntity<SystemUser>(userId)
    .Function<RetrieveUserPrivilegesResponse>("RetrieveUserPrivileges")
    .ExecuteAsync();
```

`ForEntity<T>(id)` takes the entity set from `T`'s mapping; pass it explicitly for an unmapped type:
`ForEntity<object>(userId, "systemusers")`.

## Unbound actions

Pass the parameters as a request object, or one at a time:

```csharp
public class WinOpportunityRequest
{
    public OpportunityClose? OpportunityClose { get; set; }
    public int Status { get; set; }
}

public class OpportunityClose
{
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("opportunityid@odata.bind")]
    public string? Opportunity { get; set; }
}

await client.Action<WinOpportunityRequest>("WinOpportunity")
    .WithRequest(new WinOpportunityRequest
    {
        Status = 3,
        OpportunityClose = new() { Subject = "Won", Opportunity = $"/opportunities({opportunityId})" }
    })
    .ExecuteAsync();

// The same, parameter by parameter
await client.Action<WinOpportunityRequest>("WinOpportunity")
    .WithParameter(r => r.Status, 3)
    .WithParameter(r => r.OpportunityClose, new OpportunityClose { Subject = "Won", Opportunity = $"/opportunities({opportunityId})" })
    .ExecuteAsync();
```

`WithParameter(r => r.Property, value)` uses the name the serializer writes for the property, so it always matches
`WithRequest`.

`Action<TRequest>(name)` is for an action that returns nothing. For one that returns a value, name the response type
as well: `Action<TRequest, TResponse>(name)`.

An action with a few parameters does not need a request class. `Action(name)` takes them by name, exactly as the
action declares them:

```csharp
await client.Action("new_Recalculate").WithParameter("Mode", 2).ExecuteAsync();

var score = await client.Action("new_Score").WithParameter("Mode", 2).ExecuteAsync<ScoreResult>();
```

An enum parameter is written by member name, as Dataverse requires (it rejects the integer): `"ReadAccess"`, or
`"ReadAccess, WriteAccess"` for a `[Flags]` combination. As for functions, the C# enum's member names must match the
Dataverse enum's. This applies to a parameter that is itself an enum or a list of enums. An enum inside an object
parameter (a choice column of a record) is still written as its integer value.

```csharp
[Flags]
public enum AccessRights { None = 0, ReadAccess = 1, WriteAccess = 2 }

var link = await client.Action<SharedLinkRequest, SharedLinkResponse>("GenerateSharedLink")
    .WithParameter(p => p.Target, new JsonObject { ["accountid"] = accountId, ["@odata.type"] = "Microsoft.Dynamics.CRM.account" })
    .WithParameter(p => p.SharedRights, AccessRights.ReadAccess)
    .ExecuteAsync();
```

Dates can be of any `DateTime` kind: Dataverse reads a value without an offset as UTC, whatever the user's time zone.

## Bound actions

```csharp
// leads(<id>)/Microsoft.Dynamics.CRM.QualifyLead
var created = await client
    .ForEntity<Lead>(leadId)
    .Action<QualifyLeadRequest, JsonObject>("QualifyLead")
    .WithRequest(new QualifyLeadRequest { CreateAccount = true, CreateContact = true, CreateOpportunity = false, Status = 3 })
    .ExecuteAsync();
```

A `JsonObject` response type gives the raw response when it does not map neatly to a class; QualifyLead returns a
collection of the created records.

## In a batch

`ToRequest()` builds an action's request without sending it, so it can run inside a batch changeset:

```csharp
await client.Batch()
    .Add(client.Action<WinOpportunityRequest>("WinOpportunity").WithRequest(request).ToRequest())
    .ExecuteAsync();
```

## Errors

A failed call throws `DataverseException`. A 404 usually means the name is wrong, or the operation was called as the
wrong kind (an action as a function, or unbound instead of bound); a 400 means a parameter is missing or has the
wrong type. See [Error Handling](ERROR_HANDLING.md).

For the Dynamics 365 Sales actions (`QualifyLead`, `WinOpportunity`, `LoseOpportunity`, `CloseIncident`,
`ConvertQuoteToSalesOrder`, `CalculatePrice`), the `Dataverse.Fluent.Sales` package has typed wrappers.
