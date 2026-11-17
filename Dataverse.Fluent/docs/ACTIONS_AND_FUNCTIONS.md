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

Parameters are added with `WithParameter(name, value)`. Strings, numbers, booleans, GUIDs, dates (in UTC) and enums
(by integer value) are written as OData literals; any other object is sent as JSON. An entity reference is an object
with `@odata.id`:

```csharp
// CalculateRollupField(Target=@p1,FieldName=@p2)?@p1={"@odata.id":"accounts(...)"}&@p2='opendeals'
var account = await client.Function<RollupAccount>("CalculateRollupField")
    .WithParameter("Target", new Dictionary<string, string> { ["@odata.id"] = $"accounts({accountId})" })
    .WithParameter("FieldName", "opendeals")
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

await client.Action<WinOpportunityRequest, object>("WinOpportunity")
    .WithRequest(new WinOpportunityRequest
    {
        Status = 3,
        OpportunityClose = new() { Subject = "Won", Opportunity = $"/opportunities({opportunityId})" }
    })
    .ExecuteAsync();

// The same, parameter by parameter
await client.Action<WinOpportunityRequest, object>("WinOpportunity")
    .WithParameter(r => r.Status, 3)
    .WithParameter(r => r.OpportunityClose, new OpportunityClose { Subject = "Won", Opportunity = $"/opportunities({opportunityId})" })
    .ExecuteAsync();
```

`WithParameter(r => r.Property, value)` uses the name the serializer writes for the property, so it always matches
`WithRequest`. Use `object` as `TResponse` for an action that returns nothing; `ExecuteAsync` then returns `null`.

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
    .Add(client.Action<WinOpportunityRequest, object>("WinOpportunity").WithRequest(request).ToRequest())
    .ExecuteAsync();
```

## Errors

A failed call throws `DataverseException`. A 404 usually means the name is wrong, or the operation was called as the
wrong kind (an action as a function, or unbound instead of bound); a 400 means a parameter is missing or has the
wrong type. See [Error Handling](ERROR_HANDLING.md).

For the Dynamics 365 Sales actions (`QualifyLead`, `WinOpportunity`, `LoseOpportunity`, `CloseIncident`,
`ConvertQuoteToSalesOrder`, `CalculatePrice`), the `Dataverse.Fluent.Sales` package has typed wrappers.
