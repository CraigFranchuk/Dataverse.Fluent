# Design Philosophy: No Metadata Dependency

The library does not depend on the environment's entity model. Partial POCOs, pluggable type mapping and
explicit fluent operations all follow from that decision.

## The problem

Most Dataverse/Dynamics 365 client libraries are **metadata-driven**. They depend on the
server's entity data model (entities, attributes, relationships, option sets, and all their
localized labels) to function. That model is either checked into the codebase or downloaded at
runtime, and for a Dynamics 365 organization with many tables it runs to tens or hundreds of megabytes.

That dependency imposes costs that are paid on every consumer of the library:

- **Slow cold start.** The model must be fetched and/or parsed before the client is fully
  usable. On metadata-rich orgs this adds seconds to first use.
- **Large memory footprint.** The whole-org model stays resident in memory to be useful.
- **Operational burden.** A cached model goes stale when the schema changes, so you inherit a
  cache-invalidation/refresh problem you never asked for.
- **Inferred ("magic") behavior.** Because the model is present, the client uses it to *decide*
  how to execute requests at runtime — behavior you cannot see in your code.
- **Heavy for containers and short-lived processes.** A giant runtime-loaded model works against
  fast-starting, small deployments.

## Explicit intent over inferred behavior

**Dataverse.Fluent takes no dependency on the entity data model.** It never downloads,
caches, or parses org metadata. Instead:

> **The developer's POCO and explicit method calls *are* the model.**

You model only what you use, and the calls say what to do. The client never has to infer anything
from a downloaded schema.

### Worked example: alternate keys

A metadata-driven client, when you query an entity using only the fields that happen to form a
defined alternate key, will consult the model, recognize the key, and silently rewrite the
request to use OData key syntax — `accounts(altkey='X')` instead of a `$filter`. Useful, but it
*requires the model to be loaded and consulted on a hot path*, and the behavior is invisible in
your code.

This library does the same efficient thing, but **only because you asked for it explicitly**:

```csharp
// Explicit intent — emits accounts(accountnumber='ACC-001'), no metadata required.
var account = await client.Entity<Account>()
	.UsingAlternateKey("accountnumber", "ACC-001")
	.RetrieveAsync();

// A plain filter stays a plain filter — no hidden rewrite.
var account = await client.Query<Account>()
	.Where(a => a.AccountNumber == "ACC-001")
	.GetListAsync();
```

The key syntax is produced from your call, not inferred from a schema: the same capability,
with no metadata and no hidden rewrite.

## What this gives you

| Concern | Metadata-driven client | Dataverse.Fluent |
|---|---|---|
| Source of truth | Server schema (downloaded/cached) | Your POCO + explicit intent |
| Cold start | Fetch/parse hundreds of MB | Effectively instant |
| Memory | Whole-org model resident | Small per-type name caches |
| Schema drift | Cache staleness / invalidation | Compile against the POCO you own |
| Behavior | Inferred at runtime from metadata | Explicit in your code |
| Container / short-lived process fit | Heavy | Light |

### A note on Native AOT and trimming

This library is not yet AOT- or trim-compatible, and does not claim to be. It serializes POCOs with
reflection-based `System.Text.Json`, resolves names by reflecting over attributes, and may compile
expression subtrees when translating a lambda filter. Avoiding a metadata download removes one obstacle
to AOT, but these remain.

## What it costs

Without the metadata model, the library cannot do what metadata enables:

- **Automatic type/shape validation.** We do not know from a schema that a column is `Money`
  vs `Decimal`, or the valid range of an option set. You model the type on your POCO and rely
  on the server's error responses for invalid data.
- **Auto-discovery.** No automatic relationship-navigation-name discovery, no "select every
  real column" without listing them, no option-set label translation without requesting
  formatted values.
- **Inferred execution behaviors.** Things like automatic alternate-key rewriting or
  polymorphic lookup-target resolution are explicit operations here, not silent inferences.

For POCOs that model only the columns a use case needs (see
[Partial POCOs & null handling](CUSTOM_TYPE_MAPPING.md#partial-pocos--null-handling)), most of
that metadata describes columns and behaviours the code never touches.

## How the pieces fit together

The rest of the library follows from this:

- **Partial, purpose-built POCOs** — you only need to model what you use, because nothing
  validates against a full schema.
- **Null omission on writes** — unset properties are dropped from payloads, so partial POCOs
  work without "null is not allowed" errors.
- **Pluggable type mapping** ([Custom Type Mapping](CUSTOM_TYPE_MAPPING.md)) — bring types from
  any generator without a proprietary, metadata-bound code generator.
- **Explicit fluent operations** — capabilities like alternate keys, expands, and FetchXML are
  things you invoke deliberately, not behaviors inferred from a downloaded model.

## Summary

The benefit is not that every query runs faster: once a metadata-driven client has the model in memory, per-call
lookups are cheap. The benefits are no metadata download, a fast cold start, a small memory footprint, no cache to
invalidate when the schema changes, and behaviour that is visible in the code that asks for it.
