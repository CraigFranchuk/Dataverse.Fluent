# API Reference

An overview of the public API. Every member also has XML documentation, so IntelliSense shows the details,
parameters and exceptions. The guides linked below show each area in use.

All asynchronous methods take an optional `CancellationToken`. Builders are single-use: a terminal method (`...Async`
or `To...Request`) consumes the builder, and calling a terminal method again throws `InvalidOperationException`.

## Client

### `DataverseClient` / `IDataverseClient`

| Constructor | Use |
|-------------|-----|
| `DataverseClient(string baseUrl, TokenCredential credential, HttpClient? httpClient = null, ILogger? logger = null)` | Authenticate with an Azure credential |
| `DataverseClient(string baseUrl, Func<Task<string>> tokenProvider, HttpClient? httpClient = null, ILogger? logger = null)` | Authenticate with a token delegate |
| `DataverseClient(DataverseClientOptions options, HttpClient? httpClient = null, ILogger? logger = null)` | Configure resilience, scope and telemetry |
| `DataverseClient(IDataverseTransport transport)` | Run over another transport (a test fake or a decorator); the caller keeps ownership |

Without an `HttpClient`, the client creates one with the retry, timeout and token pipeline described in the
[README](../README.md#create-a-client), and disposes it with the client.

| Entry point | Returns | Guide |
|-------------|---------|-------|
| `Query<T>(entitySet?)` | `QueryBuilder<T>` | [README: Queries](../README.md#queries) |
| `Entity<T>(id?, entitySet?)` | `EntityBuilder<T>` | [README: Single records](../README.md#single-records) |
| `Aggregate<T>(entitySet?)` | `AggregateBuilder<T>` | [README: Counts](../README.md#counts) |
| `Batch(batchId?)` | `BatchBuilder` | [README: Batches](../README.md#batches) |
| `DeepInsert<T>(parent, entitySet?)` | `DeepInsertBuilder<T>` | [Deep Insert](DEEP_INSERT_GUIDE.md) |
| `Associate()` | `AssociationBuilder` | [README: Deep insert and associations](../README.md#deep-insert-and-associations) |
| `FetchBuilder<T>(entityLogicalName?, options?)` | `FetchBuilder<T>` | [FetchXML Examples](FETCHXML_EXAMPLES.md) |
| `Fetch<T>(fetchXml, options?)` | `FetchQueryExecutor<T>` | [FetchXML Examples](FETCHXML_EXAMPLES.md) |
| `Delta<T>(entitySet?)` | `DeltaBuilder<T>` | [Delta Queries](DELTA_QUERIES.md) |
| `Action<TRequest, TResponse>(name)` | `ActionBuilder<TRequest, TResponse>` | [Actions and Functions](ACTIONS_AND_FUNCTIONS.md) |
| `Function<TResponse>(name)` | `FunctionBuilder<TResponse>` | [Actions and Functions](ACTIONS_AND_FUNCTIONS.md) |
| `ForEntity<T>(id, entitySet?)` | `EntityFluentBuilder<T>` (bound `Action` / `Function`) | [Actions and Functions](ACTIONS_AND_FUNCTIONS.md) |
| `Annotation(annotationId?)` | `AnnotationBuilder` | [Annotations](ANNOTATIONS.md) |
| `Transport` | `IDataverseTransport` | [Transport](#transport) |

The entity set comes from `[DataverseEntity]`, `[EntitySet]` or a registered `IDataverseNameResolver`; pass it
explicitly for a type that has none.

### Dependency injection

| Method | Registers |
|--------|-----------|
| `AddDataverseClient(baseUrl, credential)`, `(baseUrl, tokenProvider)`, `(baseUrl, sp => tokenProvider)` | The default client |
| `AddDataverseClient(Action<DataverseClientOptions>)`, `(IConfiguration, configure?)` | The default client from options or configuration |
| `AddKeyedDataverseClient(name, configure)`, `(name, IConfiguration, configure?)` | A named client, resolved with `[FromKeyedServices(name)]` |
| `IDataverseClientFactory.CreateClient(name?)` | Creates a registered client by name at runtime |

Each returns the `IHttpClientBuilder`, so you can add handlers. The default client is resolved as `IDataverseClient`,
`DataverseClient` and `IDataverseTransport`.

### `DataverseClientOptions`

| Property | Default | Meaning |
|----------|---------|---------|
| `BaseUrl` | (required) | The Web API URL, `https://.../api/data/v9.2` |
| `Credential` / `TokenProvider` | (one required) | How to obtain tokens |
| `Scope` | `https://{host}/.default` | The token scope requested from `Credential` |
| `Resilience` | enabled | `DataverseResilienceOptions`: `Enabled`, `MaxRetryAttempts` (3), `MedianFirstRetryDelay` (2 s), `AttemptTimeout` (150 s), `TotalRequestTimeout` (10 min) |
| `IncludeQueryInTelemetry` | `false` | Log request URLs with their query string |

## Queries

### `QueryBuilder<T>`

| Method | Purpose |
|--------|---------|
| `Where(predicate)`, `Where(string)` | `$filter`; repeated calls are combined with `and` |
| `Select(selector)`, `Select(params string[])` | `$select` |
| `OrderBy(keySelector, descending)`, `OrderBy(string)` | `$orderby` |
| `Expand<TRelated>(navigation, configure?)`, `Expand(string)` | `$expand`, with nested select, filter, order and top through `ExpandBuilder` |
| `Top(n)` | `$top`: caps the total number of rows |
| `PageSize(n)` | `Prefer: odata.maxpagesize` (1 to 5,000) |
| `Count()` | Request `@odata.count` |
| `IncludeFormattedValues()` | Request display values |
| `GetListAsync()` | One page as a `List<T>` |
| `GetListWithCountAsync()` | One page and `@odata.count` |
| `GetPagedAsync()`, `GetNextPageAsync(page)` | Page by page, as `PagedResult<T>` (`Items`, `TotalCount`, `HasMorePages`) |
| `GetAllAsync(pageSize)` | Every page as one list |
| `StreamAsync(pageSize)` | Every row, one page at a time, as `IAsyncEnumerable<T>` |
| `CountAsync()` | `$count` (Dataverse caps it at 5,000) |

`DataverseFunctions` holds markers for the Dataverse query functions (`Under`, `EqualUserId`, `LastXDays`, `In`, ...)
for use inside `Where`.

### `AggregateBuilder<T>`

`Where`, `GroupBy(selector)`, `Sum`, `Average`, `Min`, `Max`, `CountDistinct` (each with an alias), `Count(alias)`,
then `GetListAsync<TResult>()` to read the rows into a type whose properties match the aliases.

## Single records

### `EntityBuilder<T>`

| Method | Purpose |
|--------|---------|
| `WithId(id)`, `UsingAlternateKey(name, value)`, `UsingAlternateKey(selector, value)` | Target the record; call `UsingAlternateKey` once per column of a composite key |
| `WithPrefer(value)` | Add a `Prefer` value |
| `DeleteAsync()`, `ToDeleteRequest()` | Delete |

The first read option continues on `EntityReadBuilder<T>`; the first write option on `EntityWriteBuilder<T>`.

### `EntityReadBuilder<T>`

`Select`, `SelectAll()` (the columns the POCO maps), `Expand`, `IncludeFormattedValues`, then:

| Terminal | Returns |
|----------|---------|
| `RetrieveAsync()` | `T?` |
| `RetrieveWithMetadataAsync()` | `DataverseEntityResult<T>`: `Entity`, `ETag`, `FormattedValues` |

### `EntityWriteBuilder<T>`

| Method | Purpose |
|--------|---------|
| `Set(selector, value)`, `Set(string, value)` | Set a column; `null` clears it |
| `SetFrom(entity)`, `SetFrom(entity, params string[] exclude)`, `SetFromExcept(entity, selectors)`, `SetFrom(entity, excludeReadOnly)` | Copy a POCO's non-null properties (see [SetFrom](SETFROM.md)) |
| `SetLookup(navigation, entitySet, id)`, `SetLookupByAlternateKey(...)`, `SetLookupByAlternateKeys(...)` | Bind a lookup (see [Lookup References](LOOKUP_REFERENCES.md)) |
| `WithETag(etag)` | Optimistic concurrency |
| `ReturnRepresentation()`, `ReturnRepresentation(selector)`, `ReturnMinimal()` | What the response contains |

| Terminal | Sends | Returns |
|----------|-------|---------|
| `CreateAsync()` | POST | `DataverseCreateResult<T>`: `Id`, `Entity`, `EntityUrl` |
| `UpdateAsync()` | PATCH with `If-Match` (`*` or the ETag) | `T?` when a representation was requested |
| `UpsertAsync(resolution)` | PATCH (see [Upsert Conflict Resolution](UPSERT_CONFLICT_RESOLUTION.md)) | `DataverseUpsertResult<T>`: `Id`, `Entity`, `Created` |
| `ToCreateRequest()`, `ToUpdateRequest()`, `ToUpsertRequest(resolution)` | nothing | The `DataverseRequest`, for a batch |

## Batches

### `BatchBuilder`

| Method | Purpose |
|--------|---------|
| `AddCreate(entity)`, `AddCreate(entity, out reference)`, `AddCreate(entity, lookupBindings)` | Create; a `BatchReference` lets later operations in the changeset bind to the new record |
| `AddUpdate(id, entity)`, `AddUpsert(id, entity)`, `AddDelete<T>(id)`, `AddDelete(entitySet, id)` | Write by ID |
| `AddDeepInsert(parent, navigation, children)`, `AddAssociate(...)` | Related records |
| `AddRetrieve<T>(id, entitySet?, select)`, `AddGet(relativeUrl)` | GET at the top level of the batch |
| `Add(request)`, `Add(request, out reference)` | A request from any builder's `To...Request()` |
| `NewChangeset()` | Start an independent transaction |
| `ContinueOnError()` | Keep going after a failed operation |
| `ExecuteAsync()` | `BatchResult`: `Results` (`ContentId`, `StatusCode`, `Body`, `EntityId`, `WasRolledBack`, `GetEntity<T>()`), `ThrowIfAnyFailed()` |

## Related records

| Builder | Methods |
|---------|---------|
| `DeepInsertBuilder<T>` | `WithRelated(entities, navigation)`, `WithRelated(entity, navigation)`, `ReturnRepresentation`, `WithPrefer`, `ExecuteAsync()` → `DataverseCreateResult<T>` |
| `AssociationBuilder` | `From(...)`, `Via(collectionNavigation)` or `ViaSingleValued(navigation)`, `To(...)`, `AssociateAsync()`, `DisassociateAsync()`; source and target by ID, alternate key or composite key |

## FetchXML

| Builder | Methods |
|---------|---------|
| `FetchBuilder<T>` | `AddAttribute`, `AddAttributes`, `AddAllAttributes`, `AddFilter(predicate, "and"\|"or")`, `AddOrder`, `Top`, `Page(page, pageSize)`, `Distinct`, `NoLock`, `BuildFetchXml()`, `ExecuteAsync()`, `StreamAsync()` |
| `FetchQueryExecutor<T>` | `WithOptions(...)`, `ExecuteAsync()`, `StreamAsync()` |

`FetchXmlOptions`: `MaxPageSize` (5,000), `IncludeFormattedValues`, `NoLock`. Queries without `top`, `page` or
`aggregate` are paged automatically with Dataverse paging cookies.

## Delta queries

`DeltaBuilder<T>`: `Since(token)`, `Select`, `IncludeFormattedValues`,
`WithHeaders`, then `ExecuteAsync()` or `StreamAsync(pageSize)`. Results are `DeltaResults<T>`: `Value`, `Deleted`
(`EntityReference` with `GetEntityId()` and `Removed.Reason`), `DeltaToken` (on the last page only), `NextLink`. There is no `Where`, `OrderBy`, `Top` or `Count`: Dataverse rejects them on delta queries.

## Actions and functions

| Builder | Methods |
|---------|---------|
| `ActionBuilder<TRequest, TResponse>` | `WithParameter(selector, value)`, `WithParameter(name, value)`, `WithRequest(request)`, `ExecuteAsync()`, `ToRequest()` |
| `FunctionBuilder<TResponse>` | `WithParameter(name, value)`, `ExecuteAsync()` |

## Notes and attachments

`AnnotationBuilder`: `Regarding(...)` (by ID or alternate key), `WithId`, `WithAlternateKey`, `WithSubject`,
`WithNoteText`, `WithFile(name, bytes or stream, mimeType?)`, `WithFileAsync`, `ReturnRepresentation`, then
`CreateAsync()`, `RetrieveAsync()`, `UpdateAsync()`, `DeleteAsync()` or `DownloadFileAsync()`. The `Annotation` model
maps the note's columns.

## Mapping

| Attribute | On | Purpose |
|-----------|----|---------|
| `[DataverseEntity(logicalName, entitySet)]` | class | Table logical name (FetchXML) and entity set (URLs) |
| `[EntitySet(name)]` | class | Entity set only |
| `[DataverseColumn(logicalName)]` or `[JsonPropertyName]` | property | Column name; otherwise the lowercased property name |
| `[DataverseLookup(logicalName, targetEntitySet)]` | `Guid`/`Guid?` property | A single-table lookup: read as `_x_value`, written as `@odata.bind` |
| `[DataverseReadOnly(operations)]` | property | Leave the column out of `SetFrom` and batch payloads for `Create`, `Update` or both |
| `[JsonIgnore]` | property | Never read, written or selected |

A property whose type is a mapped entity, or a collection of one, is a navigation property: it is read from
`$expand` and never written. `DataverseTypeMapping.RegisterResolver(IDataverseNameResolver)` adds custom naming
rules; see [Custom Type Mapping](CUSTOM_TYPE_MAPPING.md).

## Transport

`IDataverseTransport` has `BaseUrl` and one method, `SendAsync(DataverseRequest)`, which returns a
`DataverseResponse` (`StatusCode`, `GetHeader`, `EntityId`, `ReadAsync<T>()`, `ReadJsonObjectAsync()`,
`ReadStringAsync()`) and throws `DataverseException` for an unsuccessful response. `DataverseHttpTransport` is the HTTP
implementation. The extension methods `ExecuteRequestAsync(...)`, `ExecuteRequestAsync<T>(...)` and
`SendRequestAsync(HttpRequestMessage)` cover direct Web API calls:

```csharp
var whoAmI = await client.Transport.ExecuteRequestAsync(HttpMethod.Get, "WhoAmI");
```

For a test fake, `DataverseResponse.FromJson(json)` and `DataverseResponse.Empty(entityUrl: ...)` build responses.

## Errors

`DataverseException`: `StatusCode`, `ErrorCode`, `ActivityId`, `InnerError`, `RawResponse` and `Message`.
`BatchResult.ThrowIfAnyFailed()` throws it for one failed operation, or a `BatchOperationException` (with
`FailedOperations`) for several. See [Error Handling](ERROR_HANDLING.md).

## Telemetry

`DataverseTelemetry.ActivitySourceName` and `DataverseTelemetry.MeterName` name the OpenTelemetry source and meter.
The meter records `dataverse.client.requests`, `dataverse.client.duration` and `dataverse.client.errors`.
