# Dataverse.Fluent.Tool

The `dataverse-fluent` command checks a project's [Dataverse.Fluent](https://www.nuget.org/packages/Dataverse.Fluent)
models against a Dataverse environment's metadata, and fixes the mistakes that have one right answer.

```bash
dotnet tool install --global Dataverse.Fluent.Tool
```

## Validate

```bash
dataverse-fluent validate src/MyApp/MyApp.csproj --url https://yourorg.crm.dynamics.com
```

The tool builds the project, loads its models, and compares each one with the environment:

```text
Checking 8 models against https://yourorg.crm.dynamics.com/api/data/v9.2...

warning Incident.TicketNumber  DVS040  'ticketnumber' cannot be updated, but SetFrom sends it on update, where Dataverse ignores it.
        fix: mark it [DataverseReadOnly(DataverseOperation.Update)]
error   Quote.CloseDatedon  DVS010  Column 'closedatedon' does not exist on quote.
        Did you mean 'closedon' or 'createdon'?
error   Quote.ExpiresOn  DVS011  'expireson' is a date-only column (Edm.Date); Dataverse rejects a date and time for it on write (HTTP 400).
        Use DateOnly?.

8 models checked: 2 errors, 1 warning, 0 info. 1 can be fixed with 'dataverse-fluent fix'.
```

The exit code is 0 when there are no errors, 1 when there are (or warnings, with `--warnings-as-errors`), and 2 when the
tool could not run. That makes `validate` a CI step.

The project can also be a directory that holds one `.csproj`, or a built `.dll`.

## Fix

```bash
dataverse-fluent fix src/MyApp/MyApp.csproj --url https://yourorg.crm.dynamics.com --dry-run
dataverse-fluent fix src/MyApp/MyApp.csproj --url https://yourorg.crm.dynamics.com
```

`fix` applies the corrections that are mechanical, then rebuilds and checks again:

- the entity set of `[DataverseEntity]` or `[EntitySet]`;
- the target entity set and `NavigationProperty` of `[DataverseLookup]`;
- missing `[DataverseLookupTarget]`s, and targets with the wrong entity set or navigation property;
- `[DataverseReadOnly]` on columns Dataverse sets itself;
- a lookup mapped by its `_x_value` name, which becomes `[DataverseLookup]`.

It changes only string-literal attribute arguments, and puts each new attribute on its own line with the indentation of
the code around it. It keeps each file's encoding and line endings. `--dry-run` prints the changes as a diff and writes
nothing. A finding that needs a decision, such as a column that does not exist or a property whose type must change, is
listed for you to change by hand.

## Signing in

The tool signs in with `DefaultAzureCredential`, so any of these works without extra options:

- `az login` (Azure CLI), or a Visual Studio or VS Code sign-in;
- a service principal in `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` and `AZURE_TENANT_ID`, for CI;
- a managed identity.

Pass `--interactive` to sign in with a browser, and `--tenant-id` to choose the tenant. The account needs to read
metadata and the tables the models map. The tool only reads; it never writes to the environment.

## Options

| Option | Meaning |
|---|---|
| `--url`, `-u` | The environment URL. Default: the `DATAVERSE_URL` environment variable |
| `--interactive` | Sign in with a browser |
| `--tenant-id` | The Microsoft Entra tenant |
| `--api-version` | The Web API version. Default: `v9.2` |
| `--type`, `-t` | Check only this model (full or simple name); repeat for several |
| `--resolver` | The full name of an `IDataverseNameResolver` in the project to register first, for models mapped by a resolver |
| `--ignore` | Leave out findings with this code, e.g. `DVS015`; repeat for several |
| `--warnings-as-errors` | Exit with 1 on warnings too |
| `--no-select-check` | Skip the live `$select` check |
| `--configuration`, `-c` | The build configuration. Default: `Debug` |
| `--framework`, `-f` | The target framework of a multi-targeted project. Default: its first |
| `--no-build` | Use the existing build output |
| `--dry-run` | `fix` only: print a diff and write nothing |

The same checks run in a test with the
[Dataverse.Fluent.Schema](https://www.nuget.org/packages/Dataverse.Fluent.Schema) package. See
[Schema Validation](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/SCHEMA_VALIDATION.md)
for every finding code.

Licensed under the [MIT License](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/LICENSE).
