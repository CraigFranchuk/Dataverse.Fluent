# Dataverse.Fluent.Schema

Checks [Dataverse.Fluent](https://www.nuget.org/packages/Dataverse.Fluent) models against an environment's metadata.
A model whose column does not exist, whose type cannot hold the column, or whose lookup binds through the wrong
navigation property usually fails quietly: a retrieve leaves the property `null`, and Dataverse ignores a value it
cannot write. The validator finds these mistakes in a test, before they reach production.

```bash
dotnet add package Dataverse.Fluent.Schema
```

## In an integration test

```csharp
using Dataverse.Fluent.Schema;

[Fact]
public async Task Models_MatchTheEnvironment()
{
    var report = await DataverseSchemaValidator.ValidateAsync(client.Transport, typeof(Account).Assembly);

    report.ThrowIfAny(SchemaSeverity.Warning);
}
```

`ValidateAsync` checks every model in the assembly: each class mapped to a table with `[DataverseEntity]`,
`[EntitySet]` or a registered `IDataverseNameResolver`. Pass a list of types to check only some of them. The test fails
with every finding in its message:

```text
The models do not match the environment (3 findings):
  ERROR Quote.CloseDatedon: DVS010 Column 'closedatedon' does not exist on quote. Did you mean 'closedon'?
  ERROR Quote.ExpiresOn: DVS011 'expireson' is a date-only column (Edm.Date); Dataverse rejects a date and time for it on write (HTTP 400). Use DateOnly?.
  WARNING Incident.CustomerId: DVS024 'customerid' can also point at contact; a retrieved record pointing at it cannot be written back. Fix: add [DataverseLookupTarget("contact", "contacts", "customerid_contact")].
```

Validation sends `GET` requests only: `EntityDefinitions` metadata, and one retrieve of a random ID per model that
selects every mapped column (a 404 proves Dataverse accepts them all). It reads no records and changes nothing. The
caller needs read access to metadata and to the tables.

Columns that exist in Dataverse but are not on a model are never reported. A model only needs the columns it uses.

## What it checks

- **Tables:** the table exists, and the model's entity set is the table's.
- **Columns:** each column exists (with a suggestion for a misspelled name), is not the display value of another column
  (`createdbyname`), and fits the property's type. For example, a date-only column must be `DateOnly`, and money must
  be `decimal`. An enum mapped to a choice column is compared with its options.
- **Lookups:** the column is a lookup, the target entity set and navigation property are the ones Dataverse uses, and a
  polymorphic lookup maps exactly the tables the column can point at. A lookup mapped as a plain column, or by its
  `_x_value` name, is reported because it is never filled or cannot be written.
- **Navigation properties:** each one exists, and leads to the table its type maps.
- **Writes:** `SetFrom` does not send a column Dataverse sets itself, such as `createdon`, or one that cannot be
  updated, such as `ticketnumber`. Dataverse ignores these values silently.

Every finding has a code (`SchemaCodes`), a severity, a message and, when the validator can tell, a suggestion. Findings
that can be corrected mechanically carry a `SchemaFix`, which the `dataverse-fluent fix` command applies to the source.

## Options

```csharp
var options = new SchemaValidationOptions
{
    CheckSelect = false,        // skip the live $select retrieve
    CheckChoiceEnums = false,   // skip comparing enums with choice options
};
options.IgnoreCodes.Add(SchemaCodes.OptionWithoutEnumMember);

var report = await DataverseSchemaValidator.ValidateAsync(client.Transport, [typeof(Account)], options);
```

## From the command line

The `Dataverse.Fluent.Tool` package runs the same checks on a project and applies the mechanical fixes:

```bash
dotnet tool install --global Dataverse.Fluent.Tool
dataverse-fluent validate src/MyApp/MyApp.csproj --url https://yourorg.crm.dynamics.com
dataverse-fluent fix src/MyApp/MyApp.csproj --url https://yourorg.crm.dynamics.com --dry-run
```

See [Schema Validation](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/SCHEMA_VALIDATION.md)
for every finding code.

Licensed under the [MIT License](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/LICENSE).
