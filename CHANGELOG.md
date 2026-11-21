# Changelog

All notable changes to the `Dataverse.Fluent`, `Dataverse.Fluent.Sales`, `Dataverse.Fluent.Testing`,
`Dataverse.Fluent.Schema` and `Dataverse.Fluent.Tool` packages are recorded here. The packages follow
[semantic versioning](https://semver.org/).

## 1.3.0 (unreleased)

### Dataverse.Fluent

- `Expand(x => x.Navigation, configure?)` on `Query<T>()` and `Entity<T>()` expands a relationship by the model's
  navigation property instead of by name. The name sent is the one the property is mapped to, and the related type is
  inferred for the nested `Select`, `Where` and `OrderBy`. The string overloads remain for relationships that are not
  on the model.
- `DeepInsert(...).WithRelated(x => x.Navigation, related)` and `Associate().Via<TSource>(x => x.Navigation)` take a
  navigation property in the same way. `Via` treats a property that holds one record as single-valued.
- A property that is not a navigation property (a column, a lookup ID or a `[JsonIgnore]` property) throws
  `ArgumentException`.
- Source compatibility: `Expand<TRelated>(null)` with a bare `null` no longer compiles, because it is ambiguous
  between the overloads. No other call is affected.
- `GetListAsync()` and `GetListWithCountAsync()` log a warning when they return a page and Dataverse has more rows,
  and the query set neither `Top()` nor `PageSize()`. They return one page of at most 5,000 rows, so such a list is
  incomplete without the call having asked for a limit; the warning names `GetAllAsync()` and `StreamAsync()`. The
  behaviour of both methods is unchanged. The warning goes to the client's logger, so a client over a custom
  `IDataverseTransport` (a fake or a decorator) logs nothing.
- `client.Action<TRequest>(name)` runs an action that returns nothing; `ExecuteAsync` returns a `Task`. It replaces
  writing `object` as the response type (`Action<TRequest, object>`), which still works.
- `client.Action(name)` runs an action without a request class: parameters are set by name with `WithParameter`, and
  `ExecuteAsync()` or `ExecuteAsync<TResponse>()` sends it.
- Both are also on `ForEntity<T>(id)` for bound actions, and both have `ToRequest()` for use in a batch.

### Dataverse.Fluent.Sales

- New models: `SalesOrder`, `SalesOrderDetail`, `Invoice`, `InvoiceDetail`, `Product` and `PriceLevel`, with their
  lookups, read-only columns, date-only columns and line-item navigation properties (`SalesOrder.Details`,
  `Invoice.Details`), and enums for their choices (`SalesOrderState`/`SalesOrderStatus`,
  `InvoiceState`/`InvoiceStatus`, `ProductState`/`ProductStatus`, `PriceLevelState`/`PriceLevelStatus`,
  `PaymentTerms`, `ShippingMethod`, `FreightTerms`, `ProductStructure`, `ProductType`). They are checked against a
  live environment with the schema validator.
- New actions on `SalesClient`: `WinQuoteAsync`, `CloseQuoteAsync`, `FulfillSalesOrderAsync`,
  `CancelSalesOrderAsync` and `ConvertSalesOrderToInvoiceAsync`, which returns the created `Invoice`.
- Every action has an overload that takes its request object, such as
  `QualifyLeadAsync(leadId, QualifyLeadRequest, ct)` and `WinOpportunityAsync(WinOpportunityRequest, ct)`, and the
  short form calls it. `CalculatePriceAsync(CalculatePriceRequest, ct)` is the request form of the two price
  calculations.
- The request classes carry the parameters they lacked: `QualifyLeadRequest.OpportunityCustomerId`,
  `OpportunityCurrencyId` and `SourceCampaignId` (built with the new `ActionRecord.Reference`), and
  `ConvertQuoteToSalesOrderRequest.QuoteCloseDate`, `QuoteCloseStatus`, `QuoteCloseSubject` and
  `QuoteCloseDescription`, which close an active quote as part of the conversion.
- The `SalesClient` methods are virtual, so a derived client can change an action by overriding the overload that
  takes the request.
- **Breaking:** `ConvertQuoteToSalesOrderAsync` returns the created order as a `SalesOrder` instead of a
  `ConvertQuoteToSalesOrderResponse`, which is removed. `SalesOrderId`, `Name`, `OrderNumber` and `TotalAmount`
  have the same names on `SalesOrder`.
- **Breaking:** the state, status reason and other standard choice columns of the Sales models are enums instead of
  `int`. The values were read from a live environment's metadata and are checked against it by the integration tests.
  - `StateCode` and `StatusCode` on `Account`, `Contact`, `Lead`, `Opportunity`, `Quote` and `Incident`:
    `AccountState`/`AccountStatus`, `ContactState`/`ContactStatus`, `LeadState`/`LeadStatus`,
    `OpportunityState`/`OpportunityStatus`, `QuoteState`/`QuoteStatus`, `IncidentState`/`IncidentStatus`.
  - `Account.IndustryCode` and `Lead.IndustryCode` (`Industry`), `Lead.LeadSourceCode` (`LeadSource`),
    `Lead.PurchaseTimeFrame` (`PurchaseTimeFrame`), `Opportunity.RatingCode` (`OpportunityRating`),
    `Incident.PriorityCode` (`IncidentPriority`), `Incident.CaseOriginCode` (`CaseOrigin`) and
    `Incident.SatisfactionCode` (`CustomerSatisfaction`).
  - To migrate, compare with the enum member instead of the number (`o.StateCode == OpportunityState.Won`), or cast.
    An option an organisation added is read as its number and written with a cast, such as `(LeadStatus)100000001`.
  - `Incident.CaseTypeCode`, `Incident.SeverityCode`, `Opportunity.PriorityCode` and `Opportunity.SalesStageCode`
    stay `int?`: their shipped options are placeholders that each organisation defines.
- **Breaking:** `QualifyLeadAsync` takes `LeadStatus status = LeadStatus.Qualified` and `CloseIncidentAsync` takes
  `IncidentStatus status = IncidentStatus.ProblemSolved`, instead of `int`.
- **Breaking (binary):** `LoseOpportunityAsync` has a new `status` parameter before the cancellation token,
  `OpportunityStatus.Canceled` by default as before, so an opportunity can be lost as `OutSold`. Recompile; a call
  that passes the cancellation token by position must name it.

## 1.2.0 (2026-11-20)

Two new packages check models against an environment's metadata at design time. The client itself still never reads
metadata. See [Schema Validation](Dataverse.Fluent/docs/SCHEMA_VALIDATION.md).

### Dataverse.Fluent.Schema (new)

- `DataverseSchemaValidator.ValidateAsync(transport, assembly or types)` compares each model with `EntityDefinitions`
  and returns a `SchemaReport` of findings. Each finding has a code (`SchemaCodes`), a severity, a message, a
  suggestion where one is known, and a `SchemaFix` where the correction is mechanical. `ThrowIfAny(severity)` fails a
  test with every finding in its message. It checks:
  - tables and entity sets;
  - that each column exists, with "did you mean" suggestions;
  - that no property maps the display value of another column (`createdbyname`);
  - property types against column types (a date-only column must be `DateOnly`, money `decimal`);
  - enum members against the options of a choice column;
  - lookups: target entity sets, navigation properties, polymorphic targets, and lookups mapped as plain columns or by
    their `_x_value` name;
  - navigation properties, and the table each one's type maps;
  - columns that `SetFrom` sends but Dataverse ignores.
- A live `$select` check retrieves a random ID per model with every mapped column selected. A 404 proves Dataverse
  accepts them all. Validation sends `GET` requests only.
- Columns that exist in Dataverse but are not on a model are never reported.
- `DataverseModelScaffolder.ScaffoldAsync(transport, request)` describes how named properties of a table should be
  mapped: the column each one matches (by name, ignoring case), the type that holds it and whether a given type fits,
  lookup targets with their entity sets and navigation properties, read-only operations, and the options of a choice
  column. A property that matches no column comes back as `NotFound` with the closest names.

### Dataverse.Fluent.Tool (new)

- The `dataverse-fluent` dotnet tool. `validate <project>` builds a project and prints its models' findings, and exits
  with 1 when there are errors (or warnings, with `--warnings-as-errors`), so it can run in CI. `fix <project>` applies
  the mechanical fixes to the source, then rebuilds and checks again; `--dry-run` prints a diff instead. A fix changes
  only string-literal attribute arguments and keeps each file's encoding and line endings. It signs in with
  `DefaultAzureCredential`, or with a browser via `--interactive`.
- `scaffold <project>` fills in the project's models from the environment. For each class with `[DataverseEntity]`
  or `[EntitySet]`, a property without a mapping is matched to a column by name and gets `[DataverseColumn]` or
  `[DataverseLookup]` (with its targets), `[DataverseReadOnly]` where Dataverse does not accept a write, and a new type
  when its own cannot hold the column. `[DataverseEntity("incident")]` gets its entity set. The project is read from
  source and not built, so a model does not have to compile first; a second run changes nothing. `--enums` writes an
  enum per choice column. `scaffold --table incident --columns title,customerid --output Models/Incident.cs` writes a
  new model instead. (#4)
- `scaffold` reads the source the way the compiler does, without a restore or a build: constants, aliases and your own
  types are understood, and a name that does not resolve is taken as it is written. A property it deliberately leaves
  for `validate` (a computed property, a column mapping to a lookup's name) is listed, and `--skip Model.Property`
  leaves out a property that is not a column.
- `scaffold` and `fix` parse each edited file again before writing it. A file whose edit would break it, or leave an
  attribute on the wrong declaration, is held back with its diff printed, and the others are written; the exit code is
  2. `--force` writes it anyway.

### Dataverse.Fluent

- `DataverseEntityMapping.For<T>()` describes a type as the builders read and write it: its table and entity set, and
  for each property its kind, names, lookup targets, read-only markers, related type, and whether `SetFrom` sends it on
  create and on update. The schema validator is built on it.
- `DataverseTypeMapping.IsMapped(type)` tells whether an attribute or a registered resolver maps a type to a table. It
  reads none of the type's properties, so it does not throw for a type whose property mappings are invalid.
- Fixed: a model that hides a base class's property with `new` and a different type threw `ArgumentException` when the
  client first used it. The derived property is now the model's, as it is for System.Text.Json.

### Dataverse.Fluent.Sales

- The live tests check every Sales model with the schema validator, in place of the hand-written metadata comparison
  added in 1.1.1. No model changed.

## 1.1.1 (2026-11-20)

Each fix follows from a check against a live environment.

### Dataverse.Fluent

- Fixed: a boolean column used on its own as a condition, as in `Where(x => x.IsActive)`, `!x.IsActive` or
  `x.Age > 1 && x.IsActive`, did not filter. OData sent the bare column (`$filter=isactive`) and FetchXML the
  attribute name as text inside the `<filter>`; Dataverse accepts both and ignores them, so every record matched,
  with the flag set or not. Such a condition is now sent as `isactive eq true` (`<condition operator="eq" value="1">`
  in FetchXML), in `Query`, `Expand`, `Aggregate` and `FetchBuilder` filters. Comparisons such as
  `x.IsActive == true` were not affected. **Queries that used the bare form return fewer rows.**
- Fixed: `Set(x => x.Property, value)` ignored a `[JsonConverter]` on the property, so it could send a different value
  from `SetFrom`, batches and bulk operations, which use the converter.
- Fixed: in a FetchXML filter, a polymorphic lookup's ID compared from the left (`id == x.Customer.Id`) was sent with
  the ID as the attribute name. The comparison is now turned around, as it is for other columns.
- Changed: the HTTP pipelines the library builds no longer store cookies. Dataverse sets an `ARRAffinity` cookie on
  every response, which sent every later request from a client to the same web server; Microsoft recommends dropping
  it for parallel requests, such as bulk operations with `MaxDegreeOfParallelism`. An `HttpClient` you pass to the
  client, or a primary handler you set with `ConfigurePrimaryHttpMessageHandler`, is not changed.
- Docs: map a date-only column (Date Only behaviour, `Edm.Date`) as `DateOnly?`. Dataverse rejects a date and time
  for it with HTTP 400. Polymorphic lookups and bulk operations are now covered in Best Practices.

### Dataverse.Fluent.Sales

This release makes breaking changes to the Sales models, marked below. The package is days old, so the models are
corrected now rather than carried in their wrong form until 2.0.

- **Breaking**, fixed: `Opportunity.EstimatedCloseDate`, `Opportunity.ActualCloseDate` and `Quote.ExpiresOn` are now
  `DateOnly?` (were `DateTime?`). They are date-only columns, and Dataverse rejects a date and time for them
  (HTTP 400), so a create or update with a value failed, including `SetFrom` on a retrieved record.
- **Breaking**, fixed: `Quote.CloseDatedon` is removed. It mapped `closedatedon`, which does not exist, so it was never
  read and a request that selected, filtered or wrote it failed. The close date is the new `Quote.ClosedOn`
  (`closedon`, a `DateOnly?`).
- **Breaking:** the polymorphic lookups are now `DataverseReference?` (were read-only `Guid?`): `CustomerId` on
  `Incident`, `Opportunity` and `Quote`, `Contact.ParentCustomerId`, and `OwnerId` on every model. A read returns the
  table as well as the ID, and `Set`, `SetFrom`, batches and bulk operations write the bind for the table the
  reference points at, so `SetLookup("customerid_account", ...)` is no longer needed. Filter by ID with
  `x.CustomerId!.Id == id` (was `x.CustomerId == id`). `SetFrom` sends `OwnerId` on create only, so writing back a
  retrieved record never reassigns it; reassign with `Set(x => x.OwnerId, reference)`.
- **Breaking:** `Lead.EstimatedRevenue` is renamed `AnnualRevenue`. It maps `revenue`, the company's annual revenue,
  not the lead's estimated value; that is the new `Lead.EstimatedAmount` (`estimatedamount`).
- Fixed: `Incident.SatisfactionCode` mapped `satisfactioncode`, which does not exist; it now maps
  `customersatisfactioncode`. Before, it was never read, and a request that selected, filtered or wrote it failed.
- Fixed: columns Dataverse sets itself are marked `[DataverseReadOnly]`, so `SetFrom` no longer sends values that
  Dataverse silently ignores: `Contact.FullName`, `Lead.FullName`, `Incident.EscalatedOn` and `Quote.RevisionNumber`
  are never sent, and `Incident.TicketNumber`, `Quote.QuoteNumber` and `IsProductOverridden` on opportunity and quote
  lines are sent on create only.

## 1.1.0 (2026-11-19)

### Dataverse.Fluent

- `WithOptions` returns a client that sends Dataverse's optional request headers with every request:
  `BypassBusinessLogic` (`CustomSync`/`CustomAsync`), `BypassPluginStepIds`, `BypassPowerAutomateFlows`,
  `DetectDuplicates`, `SolutionUniqueName`, impersonation with `CallerObjectId` or `CallerSystemUserId`, and any other
  header through `Headers`. Batches built from that client carry the headers in every operation, because Dataverse
  does not apply the headers of a `$batch` request to the operations inside it.
  See [Request Options](Dataverse.Fluent/docs/REQUEST_OPTIONS.md).
- `Bulk<T>()` writes many records with `CreateMultiple`, `UpdateMultiple` and `UpsertMultiple` (by primary key or
  alternate key, including composite keys). Records are sent in chunks (`ChunkSize`, default 100), optionally in
  parallel (`MaxDegreeOfParallelism`). The `BulkResult` reports each chunk as succeeded, failed or not sent, with the
  created IDs in input order, and `ThrowIfAnyFailed()` throws `BulkOperationException`. Missing and duplicate keys are
  rejected before anything is sent. See [Bulk Operations](Dataverse.Fluent/docs/BULK_OPERATIONS.md).
- Polymorphic lookups (customer, owner, regarding) map as a `DataverseReference` (table logical name and ID) with
  `[DataverseLookup("column")]` and one `[DataverseLookupTarget(logicalName, entitySet, navigationProperty)]` per
  table. `SetFrom`, `Set`, batches and bulk operations write the bind for the table the reference points at.
  Queries, retrieves, FetchXML, delta queries and batch retrieves request the `lookuplogicalname` annotation
  automatically when a type needs it, so reads fill in the table. Filters use `x.Lookup.Id`. See
  [Lookup References](Dataverse.Fluent/docs/LOOKUP_REFERENCES.md#polymorphic-lookups).
- `DataverseLookupAttribute.TargetEntitySet` is now `string?`: it is `null` for a polymorphic lookup.
- `DataverseResponse.FromEntity(record)` and `DataverseResponse.FromEntities(records, nextLink, totalCount)` build
  responses for a test fake with records in the shape Dataverse returns them: mapped column names, lookups as
  `_x_value`, and a `DataverseReference` with its `lookuplogicalname` annotation. The builders read them back unchanged.

### Dataverse.Fluent.Testing (new package)

- `FakeDataverseTransport`, a fake transport for unit testing code that uses the client. Routes match a method and a
  path (`fake.OnGet("accounts(*)")`), optionally narrowed with `When`; the route added last wins, and a request no
  route matches throws, naming the request and the routes. Responses are queued per route: records
  (`RespondWithEntities`, `RespondWithEntity` with an ETag), creates (`RespondWithCreated`, `RespondWithBulkCreated`),
  `RespondWithNoContent`, raw JSON, Dataverse errors (`RespondWithError`, thrown as `DataverseException`) and other
  exceptions. Every request is recorded with its decoded path, query options, headers and payload.
- `$batch` requests are answered operation by operation through the same routes, with changeset rollback and
  continue-on-error behaviour as Dataverse reports it. `Operations` lists every operation, batched or not.
  See the [README](Dataverse.Fluent.Testing/README.md).

## 1.0.2 (2026-11-18)

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
