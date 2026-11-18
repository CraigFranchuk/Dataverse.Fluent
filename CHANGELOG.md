# Changelog

All notable changes to the `Dataverse.Fluent` and `Dataverse.Fluent.Sales` packages are recorded here. The
packages follow [semantic versioning](https://semver.org/).

## 1.0.1 (unreleased)

### Dataverse.Fluent

- Fixed: `CountAsync()` on a query with a filter failed with `Could not find a property named '...' on type
  'Edm.Int32'`. It sent `{set}/$count?$filter=...`, and Dataverse applies a filter on the `/$count` segment to the
  count rather than to the records. A filtered count now requests one row with `$count=true` and returns
  `@odata.count`. A count without a filter still uses `/$count`.

## 1.0.0 (2026-11-16)

The first release.

### Dataverse.Fluent

#### Queries

- `Query<T>()` with lambda filters translated to OData: comparison, logical and arithmetic operators, string
  functions, `in`, null checks, navigation paths, and the Dataverse query functions through `DataverseFunctions`.
  Values that do not depend on the record are evaluated on the client; an expression with no OData translation throws
  `NotSupportedException` rather than sending a different query.
- `Select`, `OrderBy` and `Expand` (with nested options) from lambdas or raw OData; `Top`, `PageSize`, `Count`.
- Paging with `GetPagedAsync`/`GetNextPageAsync`, `GetAllAsync` and `StreamAsync`, following `@odata.nextLink` and
  re-sending `odata.maxpagesize` on every page.
- `CountAsync`, `GetListWithCountAsync`, and server-side aggregation with `Aggregate<T>()` (`$apply`).

#### Single records

- Create, retrieve, update, upsert and delete by ID or alternate key (including composite keys).
- `UpdateAsync` always sends `If-Match`, so it never creates a record; ETag concurrency with `WithETag`.
- Upsert strategies: `Upsert`, `PreventUpdate`, `PreventCreate` and `IfUnmodifiedSince`. `UpsertAsync` returns the
  record's ID and, with a representation, whether it was created.
- `CreateAsync` returns the new ID from `OData-EntityId`. `ReturnRepresentation()`, optionally with `$select`, and
  `ReturnMinimal()`.
- `RetrieveWithMetadataAsync` surfaces the ETag and formatted values without modelling annotations on the POCO.
- Typed read and write stages, so options that do not apply to an operation do not compile.

#### Mapping

- `[DataverseEntity]` (table logical name and entity set) and `[EntitySet]`; `[DataverseColumn]` or
  `[JsonPropertyName]` for columns, with a lowercase fallback.
- `[DataverseLookup]`: a lookup read, selected and filtered as `_x_value` and written as `@odata.bind`.
- Navigation properties are read from `$expand` and never written, so a retrieved record can be written back.
- `SetFrom` leaves out `null` properties and those marked `[DataverseReadOnly]` for the operation.
- `IDataverseNameResolver` for mapping generated types without the library's attributes.

#### Batches and related records

- `Batch()` with transactional changesets, standalone GETs, Content-ID references between operations in a changeset,
  continue-on-error, and per-operation results that report rolled-back siblings.
- `BatchBuilder.Add` accepts a request from any builder (`ToCreateRequest`, `ToUpsertRequest`, `ToDeleteRequest`,
  `ActionBuilder.ToRequest`, ...).
- Deep insert, and associating or disassociating records through collection- and single-valued navigation properties.

#### More of the Web API

- FetchXML through `FetchBuilder<T>()` or raw XML, with automatic paging-cookie paging.
- Delta queries (change tracking) with `Delta<T>()`.
- Bound and unbound actions and functions.
- Notes and file attachments with `Annotation()`.

#### Transport, authentication and resilience

- Authentication with any `TokenCredential` or a token delegate; tokens are cached until shortly before they expire
  and refreshed when Dataverse rejects them.
- Write-safe retries: GET requests on transient failures, writes only on HTTP 429 or when the connection could not be
  established, so a write is never applied twice. `Retry-After` is honoured, with a circuit breaker and timeouts sized
  for Dataverse. Configurable through `DataverseResilienceOptions`.
- Dependency injection with `AddDataverseClient`, multiple environments with `AddKeyedDataverseClient` and
  `IDataverseClientFactory`, options bound from configuration and validated at startup.
- `IDataverseTransport`, a one-method interface every builder sends through, for test fakes and decorators;
  `DataverseHttpTransport` is the HTTP implementation.
- `DataverseException` with the HTTP status, Dataverse error code, activity ID and inner error.
- `DataverseResponse` bodies can be read more than once, so a transport decorator can inspect a response before the
  builder reads it.
- Logging through `ILogger`, and OpenTelemetry traces and metrics. Request URLs are logged without their query string
  unless `IncludeQueryInTelemetry` is set.

### Dataverse.Fluent.Sales

- Mapped models for account, contact, lead, opportunity, opportunity product, quote, quote detail and case, with their
  single-table lookups mapped.
- `SalesClient` with `QualifyLeadAsync`, `WinOpportunityAsync`, `LoseOpportunityAsync`,
  `CalculateOpportunityPriceAsync`, `CalculateQuotePriceAsync`, `CloseIncidentAsync` and
  `ConvertQuoteToSalesOrderAsync`.
- `AddDynamicsSalesClient` and `AddKeyedDynamicsSalesClient` for dependency injection.

### Requirements

- .NET 10 or later. The unit tests run on .NET 10 and .NET 11.
- Not compatible with Native AOT or trimming: serialization and name resolution use reflection.
