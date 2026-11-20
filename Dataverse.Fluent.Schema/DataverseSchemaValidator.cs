using System.Reflection;
using Dataverse.Fluent.Schema.Metadata;
using Dataverse.Fluent.Schema.Validation;

namespace Dataverse.Fluent.Schema;

/// <summary>
/// Checks models against an environment's metadata: that each mapped table, column, lookup and navigation property
/// exists under the name the model uses, that each property's type can hold its column, and that <c>SetFrom</c> does not
/// send values Dataverse ignores.
/// </summary>
/// <remarks>
/// <para>
/// Validation reads metadata with <c>GET</c> requests only (<c>EntityDefinitions</c>, plus one retrieve of a random ID
/// per model for the <c>$select</c> check), and changes nothing. Run it from an integration test or a CI step; the
/// client itself never reads metadata.
/// </para>
/// <para>
/// Columns that exist in Dataverse but are not on a model are never reported: a model only needs the columns it uses.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Fact]
/// public async Task Models_MatchTheEnvironment()
/// {
///     var report = await DataverseSchemaValidator.ValidateAsync(client.Transport, typeof(Account).Assembly);
///     report.ThrowIfAny(SchemaSeverity.Warning);
/// }
/// </code>
/// </example>
public static class DataverseSchemaValidator
{
    /// <summary>Validates every model in <paramref name="assembly"/> (see <see cref="FindModels"/>).</summary>
    /// <param name="transport">The environment to compare with, for example <c>client.Transport</c>.</param>
    /// <param name="assembly">The assembly that holds the models.</param>
    /// <param name="options">Which checks to run and which findings to leave out.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The findings. Validation never throws for a mismatch; call <see cref="SchemaReport.ThrowIfAny"/>.</returns>
    /// <exception cref="Models.DataverseException">A metadata request failed, for example because the caller cannot read metadata.</exception>
    public static Task<SchemaReport> ValidateAsync(IDataverseTransport transport, Assembly assembly, SchemaValidationOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return ValidateAsync(transport, FindModels(assembly), options, ct);
    }

    /// <summary>Validates the given models.</summary>
    /// <param name="transport">The environment to compare with, for example <c>client.Transport</c>.</param>
    /// <param name="models">The model types. A type that is not mapped to a table is reported as <see cref="SchemaCodes.InvalidMapping"/>.</param>
    /// <param name="options">Which checks to run and which findings to leave out.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The findings. Validation never throws for a mismatch; call <see cref="SchemaReport.ThrowIfAny"/>.</returns>
    /// <exception cref="Models.DataverseException">A metadata request failed, for example because the caller cannot read metadata.</exception>
    public static async Task<SchemaReport> ValidateAsync(IDataverseTransport transport, IEnumerable<Type> models, SchemaValidationOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(models);
        options ??= new SchemaValidationOptions();

        var types = models.Distinct().ToList();
        using var reader = new SchemaReader(transport, options.MaxDegreeOfParallelism);
        var results = await Task.WhenAll(types.Select(t => new ModelChecker(reader, options, t).CheckAsync(ct))).ConfigureAwait(false);

        // A property inherited by several models is reported once, against the model that declares it.
        var seen = new HashSet<(string, Type, string?, string)>();
        var findings = results.SelectMany(r => r)
            .Where(f => seen.Add((f.Code, f.Model, f.Property?.Name, f.Message)))
            .ToList();

        return new SchemaReport(types, findings);
    }

    /// <summary>
    /// The models in <paramref name="assembly"/>: its non-abstract, non-generic classes that are mapped to a table, or
    /// whose mapping attributes are invalid (so they are reported rather than skipped).
    /// </summary>
    /// <param name="assembly">The assembly to search.</param>
    public static IReadOnlyList<Type> FindModels(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = [.. ex.Types.OfType<Type>()];
        }

        return types
            .Where(t => t is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
                && !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false)
                && IsModel(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsModel(Type type)
    {
        try
        {
            return DataverseEntityMapping.For(type).IsMapped;
        }
        catch (InvalidOperationException)
        {
            // Only invalid mapping attributes throw, so the type uses the library's attributes.
            return true;
        }
    }
}
