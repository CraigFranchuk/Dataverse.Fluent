# Changelog

All notable changes to the `Dataverse.Fluent` and `Dataverse.Fluent.Sales` packages are recorded here. The
packages follow [semantic versioning](https://semver.org/).

## 1.0.2 (unreleased)

Filter translation was checked construct by construct against a live environment. These fixes follow from it.

### Dataverse.Fluent

- Fixed: `Contains`, `StartsWith` and `EndsWith` in lambda filters (OData and FetchXML) did not escape the SQL
  wildcards Dataverse applies, so `Name.Contains("a_b")` also matched `aXb`, and `Contains("[x")` matched unrelated
  records. `%`, `_` and `[` are now escaped and the text matches literally. Use a raw `Where("...")` filter for wildcard
  patterns. **Queries that relied on the wildcards return fewer rows.**
- Fixed: `list.Contains(x.Column)` was sent as the OData `in` operator, which Dataverse rejects (HTTP 501). It is now
  sent as the equivalent `Microsoft.Dynamics.CRM.In` query function.
- Fixed: `DataverseFunctions.In`, `NotIn`, `ContainValues` and `DoesNotContainValues` failed with "Invalid JSON" when a
  value contained an apostrophe. The values are now sent as a JSON array of double-quoted strings.
- Changed: filters using arithmetic (`+`, `-`, `*`, `/`, `%`) or the string functions `ToLower`, `ToUpper`, `Trim`,
  `Length`, `Substring`, `IndexOf` and `string.Concat` were sent to Dataverse, which rejects them (HTTP 501). They now
  throw `NotSupportedException` before the request is sent, with the reason. The 1.0.0 notes listed them as supported;
  they never worked.
- Changed: `Aggregate<T>().GroupBy(...)` and the aggregate methods throw `NotSupportedException` for a lookup column,
  which OData `$apply` cannot group or aggregate by (Dataverse returned HTTP 400). Use a FetchXML aggregate instead.
- Fixed: `SetFromExcept(x => x.Lookup)` and `SetFrom(entity, "lookupcolumn")` did not exclude a lookup, because a
  lookup is written under its `@odata.bind` name rather than the name it is read by.
- Fixed: retrieving, updating, upserting, deleting or associating a record by an alternate key containing `&`, `?` or
  `+` always failed with an HTML error page, because IIS rejects these characters in a URL path. Such a request is now
  sent inside `$batch`, where it works. The caller sees the same result or `DataverseException` as from a direct
  request.
- Fixed: `SetLookupByAlternateKey` (and an annotation's "regarding" alternate key) percent-encoded the key, but a bind is
  in the JSON body, which Dataverse does not URL-decode. A key with a space, `&`, `+` or `%` was looked up literally
  (`a%20b`) and not found. The key is now written as is.
- Changed: an alternate key containing a character Dataverse cannot accept throws `ArgumentException` before anything
  is sent: `/` and `\` when addressing a record, and `/`, `\`, `#` and `?` in a bind. Dataverse rejected these
  requests anyway.
- Fixed: a query whose URL was too long, such as an `In` filter with about 700 IDs or FetchXML with about 500, failed with
  HTTP 400 or 414 and an HTML error page. A direct GET fails above about 29,700 characters, below the documented
  32,768. Such a request, and its next links and FetchXML pages, is now sent inside `$batch`, which accepts URLs of up
  to about 64,000 characters. Longer requests throw `InvalidOperationException` before sending, suggesting the query be
  split.
- Fixed: function parameters of type `Guid`, `DateTime` and `DateTimeOffset` were sent quoted, and enums as integers,
  so every such call failed with "Expression of type 'Edm.String' (or 'Edm.Int32') cannot be converted…". GUIDs and
  dates are now unquoted (dates in UTC), `DateOnly` is supported, and an enum is sent as
  `Microsoft.Dynamics.CRM.{Type}'{Member}'` (comma-separated for `[Flags]`). The C# enum's type and member names must
  match Dataverse's; an undefined value throws `ArgumentException`.
- Fixed: `Top(n).GetListAsync()` and `GetListWithCountAsync()` with `n` above 5,000 returned only 5,000 rows, with no
  sign that more existed. Dataverse returns at most 5,000 rows per response and answers a larger `$top` with a next
  link. These methods now follow the next links to return up to `n` rows.
- Fixed: `DataverseException.ActivityId` was always `null`, because it read `x-ms-activity-id`, a header Dataverse
  does not send. It now reads the request ID from `x-ms-service-request-id` (or `REQ_ID`), and for an operation that
  failed inside `$batch`, that operation's own `REQ_ID`.
- Fixed: when IIS rejected a request before it reached Dataverse, the exception message was the whole HTML error page.
  It is now a one-line message with the page title. The page is still in `RawResponse`.
- Fixed: a 401 had an empty body, so the message gave no reason. It now includes the reason from Dataverse's
  `401_Error_Reason` header.
- Fixed: a delta query with `Since(token)` reported a deleted record as a changed one, an item in `Value` with an
  empty ID, and left `Deleted` empty. Dataverse sends a deletion as a `$deletedEntity` with a bare `id`, not the
  `@removed` form the library looked for. Deletions are now in `Deleted`, with `GetEntityId()` and `Removed.Reason`.
- **Breaking:** removed `DeltaBuilder.Where`, `OrderBy`, `Top` and `Count`, and `DeltaResults.Count`. Dataverse rejects
  `$filter`, `$orderby`, `$top` and `$count` on every change-tracking query, so a delta query using them always failed
  (and `DeltaResults.Count` was never set). Filter and order the changes in code, and use `StreamAsync(pageSize)` for
  page size. This breaks semantic versioning for a patch release; the methods never worked.
- Fixed: a lambda filter with the value on the left, such as `10 < x.Employees` or `null == x.Lookup`, failed. OData
  sent `10 lt numberofemployees`, which Dataverse rejects ("The right side of the operator must be a constant value"),
  and FetchXML sent the value as the attribute name (`attribute="10"`). The comparison is now turned around
  (`numberofemployees gt 10`).
- Fixed: `ids.Contains(x.NullableColumn.Value)`, the usual way to test a nullable lookup against a list of `Guid`s,
  threw `NotSupportedException` in OData filters. FetchXML already accepted it.
- Fixed: an enum action parameter (such as `AccessRights` for `GenerateSharedLink`) was sent as its integer value,
  which Dataverse rejects ("Cannot read the value '1' as a quote"). `WithParameter` and `WithRequest` now send it by
  member name (`"ReadAccess"`, or `"ReadAccess, WriteAccess"` for flags), and an undefined value throws
  `ArgumentException`. An enum inside an object parameter, a choice column, is still sent as an integer.
- Fixed: `Annotation().CreateAsync()` without `ReturnRepresentation()` returned an `Annotation` whose `AnnotationId`
  was `Guid.Empty`. Dataverse returns no body then, and the ID in the `OData-EntityId` header was not read. It is now.
- Docs: a delta token comes only with the last page. On a table with more than 5,000 rows, an initial `ExecuteAsync()`
  returns a `NextLink` and no token; use `StreamAsync()` for the initial sync.
- Docs: an upsert by alternate key does not return the record's ID (Dataverse identifies the record by key in
  `OData-EntityId`), contrary to the previous example. Use `ReturnRepresentation()` to get it.
- The package and repository URLs point to `github.com/CraigFranchuk/Dataverse.Fluent`.

## 1.0.1 (2026-11-18)

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
