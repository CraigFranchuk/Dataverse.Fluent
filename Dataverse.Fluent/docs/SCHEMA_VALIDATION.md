# Schema Validation

Check your models against an environment's metadata with `Dataverse.Fluent.Schema` in a test, or with the
`dataverse-fluent` tool from the command line. The tool also fixes the mistakes that have one right answer.

The client itself never reads metadata (see [Design Philosophy](DESIGN_PHILOSOPHY.md)). That keeps it fast to start and
free of a schema cache, but nothing checks a hand-written model either, and Dataverse often does not complain:

- A property mapped to a column that does not exist stays `null` on a retrieve without `$select`.
- A value written to a column Dataverse sets itself (`createdon`, `fullname`, `ticketnumber` after creation) is
  silently ignored.
- A date-only column typed `DateTime` reads correctly, but every write fails with HTTP 400.
- A lookup's navigation property or entity set is only wrong when the first write is sent.

Validation finds these at development or test time. It sends `GET` requests only: `EntityDefinitions` metadata, and
one retrieve of a random ID per model to check that Dataverse accepts every mapped column in a `$select`. It reads no
records and changes nothing.

## In a test

```bash
dotnet add package Dataverse.Fluent.Schema
```

```csharp
[Fact]
public async Task Models_MatchTheEnvironment()
{
    var report = await DataverseSchemaValidator.ValidateAsync(client.Transport, typeof(Account).Assembly);

    report.ThrowIfAny(SchemaSeverity.Warning);
}
```

- `ValidateAsync(transport, assembly)` checks every model in the assembly (see `DataverseSchemaValidator.FindModels`):
  each non-abstract class mapped to a table with `[DataverseEntity]`, `[EntitySet]` or a registered
  `IDataverseNameResolver`, and each class whose mapping attributes are invalid.
- `ValidateAsync(transport, types)` checks the given types.
- `SchemaReport.Findings` lists what was found. `ThrowIfAny(minimum)` throws `SchemaValidationException` with every
  finding in its message, and `ToString()` prints them.

```csharp
var options = new SchemaValidationOptions
{
    CheckSelect = false,        // skip the live $select retrieve
    CheckChoiceEnums = false,   // skip comparing enums with choice options
    MaxDegreeOfParallelism = 8, // tables read at the same time (default 4)
};
options.IgnoreCodes.Add(SchemaCodes.OptionWithoutEnumMember);
```

Columns that exist in Dataverse but are not on a model are never reported. A model only needs the columns it uses.

## From the command line

```bash
dotnet tool install --global Dataverse.Fluent.Tool

dataverse-fluent validate src/MyApp/MyApp.csproj --url https://yourorg.crm.dynamics.com
dataverse-fluent fix src/MyApp/MyApp.csproj --url https://yourorg.crm.dynamics.com --dry-run
dataverse-fluent fix src/MyApp/MyApp.csproj --url https://yourorg.crm.dynamics.com
```

`validate` builds the project, loads its models and prints the findings. It exits with 0 when there are no errors, 1
when there are (or warnings, with `--warnings-as-errors`), and 2 when it could not run. `fix` applies every finding's
`SchemaFix` to the source, then rebuilds and checks again; `--dry-run` prints the changes as a diff and writes nothing.
A fix only changes string-literal attribute arguments, puts each new attribute on its own line, and keeps each file's
encoding and line endings.

The tool signs in with `DefaultAzureCredential`, so `az login`, a Visual Studio sign-in, or a service principal in
`AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` and `AZURE_TENANT_ID` all work. Pass `--interactive` for a browser sign-in.
See the [tool README](../../Dataverse.Fluent.Tool/README.md) for every option.

In CI:

```yaml
- run: dotnet tool install --global Dataverse.Fluent.Tool
- run: dataverse-fluent validate src/MyApp/MyApp.csproj --warnings-as-errors
  env:
    DATAVERSE_URL: ${{ vars.DATAVERSE_URL }}
    AZURE_CLIENT_ID: ${{ vars.AZURE_CLIENT_ID }}
    AZURE_TENANT_ID: ${{ vars.AZURE_TENANT_ID }}
    AZURE_CLIENT_SECRET: ${{ secrets.AZURE_CLIENT_SECRET }}
```

Models mapped by a resolver instead of attributes are checked when the tool can create the resolver:
`--resolver MyApp.Mapping.GeneratedNameResolver` (a public parameterless constructor is needed). The tool fixes only
attributes it finds in the source.

## Findings

Severity: **error** means a read returns nothing or a write fails; **warning** means data is lost or a value is
silently ignored; **info** is worth knowing. **Fix** marks the findings that `dataverse-fluent fix` corrects.

### Tables

| Code | Severity | Finding | Fix |
|------|----------|---------|-----|
| DVS001 | error | The table does not exist, or no table has the model's entity set. The closest names are suggested. | |
| DVS002 | error | The model's entity set is not its table's (`[DataverseEntity("account", "account")]`). | Yes |
| DVS003 | error | The model's mapping attributes are invalid (for example `[DataverseLookup]` on a `string`), the model cannot be read (an attribute rejects its arguments, or a property's type cannot be loaded), or the type is not mapped to a table. | |
| DVS005 | info | The live `$select` check could not run, for example because the caller cannot read the table. | |

### Columns

| Code | Severity | Finding | Fix |
|------|----------|---------|-----|
| DVS010 | error | The column does not exist. The closest names are suggested; a name that differs only in case comes first. | |
| DVS011 | error | The property's type cannot hold the column, or Dataverse rejects it on write. See [Types](#types). | |
| DVS012 | warning | The property's type can lose part of the column's values (`double` for money, `int` for a BigInt). | |
| DVS013 | error | Dataverse rejects the column in a `$select` (found by the live check). | |
| DVS014 | warning | An enum member's value is not an option of the choice column it maps. | |
| DVS015 | info | Options of the choice column that have no enum member. | |
| DVS016 | error | The column is the display value of another one (`createdbyname`, `statecodename`, `customeridtype`). The Web API returns it as an annotation, such as `_createdby_value@OData.Community.Display.V1.FormattedValue`. | |

### Lookups

| Code | Severity | Finding | Fix |
|------|----------|---------|-----|
| DVS020 | error | The lookup column does not exist, or is not a lookup. | |
| DVS021 | error | `NavigationProperty` is not the one Dataverse binds the lookup with. Custom lookups usually bind with the schema name (`new_RegionId`). | Yes |
| DVS022 | error | The target entity set is not the entity set of the table the lookup points at. | Yes, for a single-table lookup |
| DVS023 | warning | A single-table lookup (`Guid`) is mapped on a column that can point at several tables. A record pointing at another table is read without it. | |
| DVS024 | warning | A polymorphic lookup is missing a table the column can point at, so a retrieved record pointing at it cannot be written back. | Yes |
| DVS025 | error | A polymorphic lookup maps a table the column cannot point at. | |
| DVS026 | error | A polymorphic target's entity set or navigation property is wrong. | Yes |
| DVS027 | error | A lookup is mapped as a plain column under its logical name (`[JsonPropertyName("primarycontactid")]`). Dataverse returns it as `_primarycontactid_value`, so the property is never filled. | |
| DVS028 | error | A lookup is mapped by its value name (`[JsonPropertyName("_primarycontactid_value")]`), so `SetFrom` writes it under that name, which Dataverse rejects with HTTP 400. | Yes: becomes `[DataverseLookup]`, for a single-table lookup (with `[DataverseReadOnly(DataverseOperation.Update)]` when it cannot be updated); `[DataverseReadOnly(DataverseOperation.CreateAndUpdate)]` for a lookup Dataverse sets itself (`createdby`) |

### Navigation properties

| Code | Severity | Finding | Fix |
|------|----------|---------|-----|
| DVS030 | error | The collection navigation property does not exist. Names are case-sensitive (`Incident_Tasks`). | |
| DVS031 | error | The single-valued navigation property does not exist, including the one a `...@odata.bind` property names. | |
| DVS032 | warning | The related record's type maps a different table than the relationship leads to. | |

### Writes

| Code | Severity | Finding | Fix |
|------|----------|---------|-----|
| DVS040 | warning | The column cannot be updated (`ticketnumber`, a primary key), but `SetFrom` sends it on update, where Dataverse ignores it. | Yes: `[DataverseReadOnly(DataverseOperation.Update)]` |
| DVS041 | warning | The column cannot be written at all (`createdon`, `fullname`, `versionnumber`), but `SetFrom` sends it. | Yes: `[DataverseReadOnly(DataverseOperation.CreateAndUpdate)]` |
| DVS042 | info | `SetFrom` sends an annotation property (a name with `@`), which Dataverse ignores. | Yes: `[DataverseReadOnly(DataverseOperation.CreateAndUpdate)]` |

### Types

The Web API types follow from the column types. Each row was checked against an environment's `$metadata`.

| Column | Web API | Map as |
|--------|---------|--------|
| Single line of text, multiple lines, entity name | `Edm.String` | `string` |
| Whole number | `Edm.Int32` | `int?` (`long`, `decimal` and `double` also fit) |
| Choice, status, status reason | `Edm.Int32` | `int?` or an enum based on `int` |
| Choices (multi-select) | `Edm.String` such as `"1,3"` | `string` |
| BigInt | `Edm.Int64` | `long?` |
| Decimal, currency | `Edm.Decimal` | `decimal?` |
| Floating point | `Edm.Double` | `double?` |
| Yes/no | `Edm.Boolean` | `bool?` |
| Unique identifier, lookup | `Edm.Guid` | `Guid?`, or `DataverseReference` for a polymorphic lookup |
| Date and time, Date Only behaviour | `Edm.Date` | `DateOnly?` |
| Date and time, any other behaviour (including User Local with the date-only format) | `Edm.DateTimeOffset` | `DateTime?` or `DateTimeOffset?` |
| Image | `Edm.Binary` | `byte[]` or `string` |

A property with a `[JsonConverter]` is not type-checked. Column types not in the table (file columns, for example) are
not checked.

## What it does not check

- Whether a mapping means what its name says. `Lead.EstimatedRevenue` mapped to `revenue` (the company's annual
  revenue, not the lead's value) is a valid mapping with the wrong meaning.
- Values: required columns, string lengths, number ranges.
- Columns a model leaves out.
- Behaviour that depends on data, such as plug-ins that reject a write.
