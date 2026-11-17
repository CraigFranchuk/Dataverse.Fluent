# Custom Type Mapping (Bring Your Own Generated Types)

Dataverse.Fluent does not ship its own entity code generator. Instead, name
resolution is **pluggable**: you can teach the client how to read the conventions of POCOs
produced by *any* source generator (or hand-written types), without applying the library's
own `[DataverseColumn]` / `[EntitySet]` attributes.

This is the recommended way to reuse types you already generate elsewhere instead of
maintaining a separate set of attributed POCOs.

## How resolution works

For every property and entity type, the client asks a chain of resolvers for the Dataverse
**logical name** (column) and **entity set name**:

1. Each custom `IDataverseNameResolver` you register, **in registration order**.
2. The built-in `AttributeNameResolver` (reads `[DataverseLookup]` → `[DataverseColumn]` →
   `[JsonPropertyName]` for properties, and `[DataverseEntity]` / `[EntitySet]` for types).
3. Fallback conventions:
   - Property name → the lowercased member name (Dataverse logical-name convention).
   - Entity set name → **no fallback**; an `InvalidOperationException` is thrown if nothing
	 resolves it.
   - Table logical name (FetchXML) → the lowercased type name.

A resolver returns `null` for any member or type it does not understand, deferring to the
next resolver in the chain. This means a custom resolver can handle only your generated types
and let everything else fall through to the defaults.

Results are cached for performance. Registering or clearing resolvers automatically
invalidates the cache.

## The contract

```csharp
public interface IDataverseNameResolver
{
	// Return the Dataverse logical (column) name, or null to defer.
	string? ResolvePropertyName(MemberInfo member);

	// Return the OData entity set name (e.g., "accounts"), or null to defer.
	string? ResolveEntitySetName(Type type);

	// Return the table's logical name (e.g., "account") for FetchXML, or null to defer.
	// Optional: the default implementation defers.
	string? ResolveEntityLogicalName(Type type) => null;
}
```

Implementations must be thread-safe and should return stable values for a given input.

Resolvers are registered process-wide (`DataverseTypeMapping` is static), so every client in the process maps a
given type the same way. Two clients cannot use different resolvers for the same type.

A resolver names a property; it does not make it a lookup. To get `@odata.bind` writes, use
`[DataverseLookup]` (see [Lookup References](LOOKUP_REFERENCES.md)). A resolver that returns a `_x_value` name
still gets the right FetchXML attribute name (`x`).

## Registering a resolver

Register once at application startup, before the client is used:

```csharp
DataverseTypeMapping.RegisterResolver(new MyGeneratedTypeResolver());
```

## Example: mapping early-bound–style generated types

Many Dataverse generators emit types decorated with attributes such as
`AttributeLogicalName` (per property) and `EntityLogicalName` / `EntitySetName` (per class).
You can map those without referencing the generator's assembly by matching the attribute
**by name** via reflection:

```csharp
using System.Reflection;
using Dataverse.Fluent;

public sealed class EarlyBoundNameResolver : IDataverseNameResolver
{
	public string? ResolvePropertyName(MemberInfo member)
		=> GetAttributeStringArg(member, "AttributeLogicalNameAttribute");

	public string? ResolveEntitySetName(Type type)
		// Prefer an explicit set name; otherwise pluralize the logical name.
		=> GetAttributeStringArg(type, "EntitySetNameAttribute")
		   ?? Pluralize(GetAttributeStringArg(type, "EntityLogicalNameAttribute"));

	private static string? GetAttributeStringArg(MemberInfo member, string attributeTypeName)
	{
		foreach (var attr in member.GetCustomAttributes())
		{
			if (attr.GetType().Name != attributeTypeName)
			{
				continue;
			}

			// Assumes the logical name is the first string property on the attribute.
			var prop = attr.GetType()
				.GetProperties()
				.FirstOrDefault(p => p.PropertyType == typeof(string));

			return prop?.GetValue(attr) as string;
		}

		return null;
	}

	private static string? Pluralize(string? logicalName)
	{
		if (string.IsNullOrEmpty(logicalName))
		{
			return null;
		}

		// Minimal Dataverse-style pluralization; adjust to your schema conventions.
		if (logicalName.EndsWith("y", StringComparison.Ordinal))
		{
			return string.Concat(logicalName.AsSpan(0, logicalName.Length - 1), "ies");
		}

		if (logicalName.EndsWith("s", StringComparison.Ordinal))
		{
			return logicalName + "es";
		}

		return logicalName + "s";
	}
}
```

Register it:

```csharp
DataverseTypeMapping.RegisterResolver(new EarlyBoundNameResolver());

// Now generated types work without [DataverseColumn]/[EntitySet]:
var accounts = await client.Query<MyGenerated.Account>()
	.Where(a => a.Revenue > 1_000_000)
	.GetListAsync();
```

> **Note on serialization vs. naming.** This resolver maps *names*. The client still
> serializes/deserializes via `System.Text.Json`, so generated types must be plain POCOs with
> public, JSON-serializable properties. Types that store data in an attribute bag (e.g., the
> SOAP `Microsoft.Xrm.Sdk.Entity` early-bound model) will resolve names correctly but will not
> serialize to clean Web API payloads — use plain POCO generators (for example, OpenAPI/Kiota
> output generated from the Dataverse `$metadata`) for full read/write support.

## Convention-only mapping

If your generated types use property names that already match Dataverse logical names (e.g.,
all-lowercase), you may not need a resolver for properties at all — the lowercase fallback
handles them. You typically still need a resolver (or `[EntitySet]`) to supply entity set
names, since there is no entity-set fallback.

```csharp
public sealed class EntitySetConventionResolver : IDataverseNameResolver
{
	public string? ResolvePropertyName(MemberInfo member) => null; // use default fallback

	public string? ResolveEntitySetName(Type type)
		=> type.Name.ToLowerInvariant() + "s"; // simple convention
}
```

## Partial POCOs & null handling

You do **not** need to model every Dataverse column. A common problem with generators pointed
at the Dynamics/Dataverse APIs is that they emit huge entity classes with hundreds of
properties you never use. With this library you can define **purpose-built POCOs** containing
only the columns and navigation properties a given use case requires.

This works because the client **omits null-valued properties from write payloads** rather than
sending them as JSON `null`. An explicit `null` would clear the column, and Dataverse rejects it for
required columns. Leaving the property out of the JSON means "not specified", so unset properties
never reach the API.

Null omission applies to all entity-instance write paths:

| API | Behavior for null properties |
|-----|------------------------------|
| `client.Entity<T>().SetFrom(entity)` (all overloads) | Omitted from the payload |
| `client.Batch().AddCreate/AddUpdate(entity)` | Omitted from the payload |
| `client.DeepInsert(parent)` | Omitted from the payload |

```csharp
// A minimal, purpose-built POCO — only what this use case needs.
[EntitySet("accounts")]
public class AccountSummary
{
	[DataverseReadOnly(DataverseOperation.Create)]
	[JsonPropertyName("accountid")]
	public Guid AccountId { get; set; }

	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[JsonPropertyName("revenue")]
	public decimal? Revenue { get; set; }

	[JsonPropertyName("description")]
	public string? Description { get; set; } // left null below
}

var account = new AccountSummary { Name = "Contoso Ltd", Revenue = 5_000_000 };

// Description is null, so it is NOT sent. No "null is not allowed" error.
await client.Entity<AccountSummary>().SetFrom(account).CreateAsync();
// Payload: { "name": "Contoso Ltd", "revenue": 5000000 }
```

### Clearing a field on purpose

Omitting nulls means "this property was not specified" — it does **not** clear an existing
value. When you intentionally want to clear a field on an update, use `Set` with an explicit
null, which deliberately emits a JSON `null` (the Web API contract for clearing a nullable
column):

```csharp
await client.Entity<AccountSummary>()
	.WithId(accountId)
	.Set("description", null) // emits "description": null to clear the field
	.UpdateAsync();
```

This gives you both behaviors without a global "ignore nulls" switch: `SetFrom` for
specify-what-you-set semantics, and `Set(field, null)` for explicit clears.

## Test isolation

Because resolvers are registered globally, clear them between tests that register their own:

```csharp
DataverseTypeMapping.ClearResolvers();
```
