# Dataverse.Fluent.Tool

The `dataverse-fluent` command checks a project's [Dataverse.Fluent](https://www.nuget.org/packages/Dataverse.Fluent)
models against a Dataverse environment's metadata, fixes the mistakes that have one right answer, and fills in a model's
mapping from the environment.

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
- a lookup mapped by its `_x_value` name, which becomes `[DataverseLookup]`, or `[DataverseReadOnly]` when Dataverse
  sets the lookup itself (`createdby`).

It changes only string-literal attribute arguments, and puts each new attribute on its own line with the indentation of
the code around it (or next to them, when a property's attributes are written on the property's own line). It keeps each file's encoding and line endings. `--dry-run` prints the changes as a diff and writes
nothing. A finding that needs a decision, such as a column that does not exist or a property whose type must change, is
listed for you to change by hand.

## Scaffold

`scaffold` fills in a model from the environment, for the properties you name. Write the class and the properties you
care about; they do not have to compile yet:

```csharp
[DataverseEntity("incident")]
public partial class Incident
{
    public string Title { get; set; }
    public DateTime DueDate { get; set; }
    public Guid CustomerId { get; set; }
    public DateTime? CreatedOn { get; set; }
}
```

```bash
dataverse-fluent scaffold src/MyApp/MyApp.csproj --url https://yourorg.crm.dynamics.com --dry-run
dataverse-fluent scaffold src/MyApp/MyApp.csproj --url https://yourorg.crm.dynamics.com
```

```csharp
[DataverseEntity("incident", "incidents")]
public partial class Incident
{
    [DataverseColumn("title")]
    public string Title { get; set; }

    [DataverseColumn("duedate")]
    public DateOnly? DueDate { get; set; }

    [DataverseLookup("customerid")]
    [DataverseLookupTarget("account", "accounts", "customerid_account")]
    [DataverseLookupTarget("contact", "contacts", "customerid_contact")]
    public DataverseReference? CustomerId { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [DataverseColumn("createdon")]
    public DateTime? CreatedOn { get; set; }
}
```

For every class with `[DataverseEntity]` or `[EntitySet]` in the project's source:

- **Matching:** a property without a mapping is matched to a column by name, ignoring case. One that matches no column
  is left alone and listed with the closest names, and the command exits with 1. For a property that is not a column,
  mark it `[JsonIgnore]`, or pass `--skip Incident.Summary` to have scaffold leave it alone.
- **Mapping:** a column gets `[DataverseColumn]`; a lookup to one table gets `[DataverseLookup]` with its entity set
  and navigation property; a lookup to several tables gets `[DataverseLookup]` and a `[DataverseLookupTarget]` per
  table.
- **Types:** a type that cannot hold its column, or holds it with loss, is replaced: `DateOnly?` for a date-only
  column, `decimal?` for money, `DataverseReference?` for a lookup to several tables. A type that already fits is kept
  as you wrote it, and so is a type of your own, such as an enum. Each change is listed, because it can break code
  that uses the property.
- **Read-only markers:** `[DataverseReadOnly]` is added for columns Dataverse sets itself or that cannot be updated.
- **Entity set:** filled in or corrected on `[DataverseEntity]`. One given as a constant is read but not rewritten: a
  wrong value is reported.
- **Mapped properties:** a property that already has `[DataverseColumn]` or `[JsonPropertyName]` keeps its mapping;
  only its type and read-only marker are checked. Lookups that are already mapped are left to `fix`.
- **Left as they are:** some properties are part of the model but not scaffold's to change, and are listed so that
  nothing is passed over in silence: a computed property (the client sends it as a column unless it has
  `[JsonIgnore]`), a property mapped as a column to a name that is a lookup, and a name or condition that cannot be
  read. They do not change the exit code; `validate` checks them. Related records (a property whose type is another
  model) and annotations are left too, without being listed.

Running it again changes nothing, so you can add properties to a model later and scaffold again. It reads the project's
source without building or restoring it, and only reads metadata from the environment. `--type` limits the run to the
models you name, and `--enums` writes an enum for each choice column that is an `int`, named after the class and
property, with a member per option.

Scaffold reads the source the way the compiler does, so a name given as a constant, an attribute or type used through
an alias, and a type of your own that shares a name with a framework type are all understood. The source is compiled
against the framework the tool runs on, without the project's packages; where a name does not resolve, because it comes
from a package or the code does not compile yet, scaffold goes by the name as it is written. For a project with several
target frameworks it reads the first, and says so.

Before `scaffold` or `fix` writes anything, each edited file is parsed again and held to what the edits were planned
to do. If an edit would break a file, or leave an attribute on another declaration, that file is held back: its diff is
printed so you can report it or make the change by hand, the other files are written as usual, and the exit code is 2.
This is a fault in the tool, not in your model. `--force` writes such a file anyway, with a warning.

To start without a class, name the table and its columns:

```bash
dataverse-fluent scaffold --table incident --columns title,duedate,customerid \
  --output Models/Incident.cs --namespace MyApp.Models --url https://yourorg.crm.dynamics.com
```

This writes a new file with the same mapping. Property names come from the columns' schema names. It does not overwrite
an existing file.

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
| `--ignore` | Leave out findings with this code, e.g. `DVS015`; repeat for several. A code that does not exist is an error |
| `--warnings-as-errors` | Exit with 1 on warnings too |
| `--no-select-check` | Skip the live `$select` check |
| `--configuration`, `-c` | The build configuration. Default: `Debug` |
| `--framework`, `-f` | The target framework of a multi-targeted project. Default: its first |
| `--no-build` | Use the existing build output |
| `--dry-run` | `fix` only: print a diff and write nothing |
| `--force` | `fix` only: write a file even when the check of its planned edit fails |
| `--verbose` | Print the stack trace of an unexpected error |

`scaffold` takes `--url`, `--interactive`, `--tenant-id`, `--api-version`, `--type`, `--configuration`, `--framework`,
`--dry-run`, `--force` and `--verbose` as above, and:

| Option | Meaning |
|---|---|
| `--enums` | Write an enum for each choice column, in place of `int` |
| `--skip` | Leave this property alone, e.g. `Incident.Summary` (the model's simple or full name); repeat for several. A name that matches no property is an error |
| `--table` | Write a new model for this table (its logical name) instead of filling in the project's models |
| `--columns` | With `--table`: the logical names of the columns the model gets, separated by commas |
| `--output`, `-o` | With `--table`: the file to write |
| `--namespace` | With `--table`: the namespace of the new model. Default: none |
| `--class` | With `--table`: the class name. Default: from the table's schema name |

The same checks run in a test with the
[Dataverse.Fluent.Schema](https://www.nuget.org/packages/Dataverse.Fluent.Schema) package. See
[Schema Validation](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/SCHEMA_VALIDATION.md)
for every finding code.

Licensed under the [MIT License](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/LICENSE).
