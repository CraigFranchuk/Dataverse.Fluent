# Documentation

Start with the [package README](../README.md) for installation and the quick start.

## Fundamentals

| Guide | Covers |
|-------|--------|
| [Getting Started](GETTING_STARTED.md) | App registration, authentication, a first program |
| [API Reference](API_REFERENCE.md) | Entry points, builders and mapping attributes |
| [Best Practices](BEST_PRACTICES.md) | Production guidance |
| [Design Philosophy](DESIGN_PHILOSOPHY.md) | Why there is no metadata dependency |
| [Custom Type Mapping](CUSTOM_TYPE_MAPPING.md) | Bring your own generated types via `IDataverseNameResolver` |
| [Error Handling](ERROR_HANDLING.md) | `DataverseException` and what is retried for you |

## Features

| Guide | Covers |
|-------|--------|
| [Actions and Functions](ACTIONS_AND_FUNCTIONS.md) | Bound and unbound actions and functions |
| [Delta Queries](DELTA_QUERIES.md) | Change tracking with delta tokens |
| [FetchXML Examples](FETCHXML_EXAMPLES.md) | Fluent and raw FetchXML |
| [Lookup Fields](LOOKUP_REFERENCES.md) | Mapping lookups, and setting them by ID or alternate key |
| [SetFrom](SETFROM.md) | Copying POCOs and excluding read-only properties |
| [Upsert Conflict Resolution](UPSERT_CONFLICT_RESOLUTION.md) | Upsert, prevent create/update, ETags |
| [Deep Insert](DEEP_INSERT_GUIDE.md) | Creating related records in one call |
| [Annotations](ANNOTATIONS.md) | Notes and file attachments |

## Contributing and testing

- [Integration test README](../../Dataverse.Fluent.IntegrationTests/README.md): credentials, running the live
  tests, known issues.
- [CHANGELOG](../../CHANGELOG.md)
