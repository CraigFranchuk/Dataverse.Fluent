---
name: Bug report
about: Something in Dataverse.Fluent or Dataverse.Fluent.Sales doesn't work as documented.
title: ''
labels: bug
assignees: CraigFranchuk
type: Bug

---

<!--
Before you file:
- Check the CHANGELOG. Some behaviour changes on purpose between releases, for example a filter that now throws NotSupportedException before anything is sent.
- Remove secrets and real data: access tokens, client secrets, connection strings, private environment URLs, and any customer or personal data.
-->

## Package
- [ ] Dataverse.Fluent
- [ ] Dataverse.Fluent.Sales

**Version** (exact NuGet version, including any -ci or prerelease suffix): 

## Area
<!-- e.g. Query<T>/OData filters, Aggregate, FetchXML, Create/Update/Upsert/Delete, alternate keys, lookups/SetFrom, deep insert/expand, batch, delta, actions/functions, annotations, paging/long URLs, type mapping, auth/AddDataverseClient, resilience/retries, logging -->


## What happened?


## Minimal reproduction
<!-- The POCO (with its [EntitySet], [DataverseColumn], [DataverseLookup] attributes) and the client call. Describe any data the environment needs. -->

```csharp

```

## Expected behaviour


## Actual behaviour


## Where does it fail?
- [ ] The library throws before sending anything (NotSupportedException, ArgumentException)
- [ ] Dataverse returns an error (DataverseException / BatchOperationException)
- [ ] The request succeeds but the result is wrong
- [ ] Hang, timeout or performance problem
- [ ] Build / compile / DI registration error
- [ ] Not sure

## Request sent to Dataverse
<!-- HTTP method and URL (and body, for writes or $batch). Set the Dataverse.Fluent log category to Debug to log each request, and set DataverseClientOptions.IncludeQueryInTelemetry = true to include the query string. Redact values as needed. -->

```text

```

## Exception and error details
<!-- Full exception with stack trace. For a DataverseException include StatusCode, ErrorCode, ActivityId and RawResponse. For a BatchOperationException include the inner exceptions. -->

```text

```

## Environment
- .NET version / target framework: 
- Operating system: 
- Dataverse: online / on-premises
- Web API version (from the base URL, e.g. v9.2): 
- How the client is created (TokenCredential, Func<Task<string>> token provider, DataverseClientOptions, AddDataverseClient / IDataverseClientFactory, custom IDataverseTransport or HttpClient): 

## Anything else?
<!-- Workarounds, whether it worked in an earlier version (and which), solution customisations (plugins, custom APIs, alternate keys), related issues. -->


## Checklist
- [ ] I searched the existing issues and this hasn't been reported.
- [ ] I checked the CHANGELOG and this isn't an intended behaviour change.
- [ ] I removed tokens, secrets and real customer or personal data from everything above.
