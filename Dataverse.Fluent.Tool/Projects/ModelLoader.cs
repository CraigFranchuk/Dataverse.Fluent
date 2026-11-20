using System.Reflection;
using System.Runtime.Loader;
using Dataverse.Fluent.Schema;

namespace Dataverse.Fluent.Tool.Projects;

/// <summary>
/// Loads a project's assembly, and the dependencies it brings, into a context of its own. <c>Dataverse.Fluent</c> and
/// <c>Dataverse.Fluent.Schema</c> resolve to the tool's copies, so the models' attributes are the types the validator
/// reads. Assemblies are loaded from memory, so the files stay unlocked and the project can be rebuilt after a fix.
/// </summary>
internal sealed class ModelLoader : AssemblyLoadContext
{
    private static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dataverse.Fluent",
        "Dataverse.Fluent.Schema"
    };

    private readonly AssemblyDependencyResolver _resolver;

    private ModelLoader(string assemblyPath)
        : base($"dataverse-fluent: {Path.GetFileName(assemblyPath)}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(assemblyPath);
    }

    /// <summary>Loads the assembly at <paramref name="assemblyPath"/>.</summary>
    /// <exception cref="ToolException">It was built against a newer Dataverse.Fluent than the tool's.</exception>
    public static Assembly Load(string assemblyPath)
    {
        var context = new ModelLoader(assemblyPath);
        var assembly = context.LoadFromBytes(assemblyPath);

        var tool = typeof(DataverseClient).Assembly.GetName().Version;
        var referenced = assembly.GetReferencedAssemblies().FirstOrDefault(a => string.Equals(a.Name, "Dataverse.Fluent", StringComparison.OrdinalIgnoreCase))?.Version;
        if (referenced is not null && tool is not null && referenced > tool)
        {
            throw new ToolException(
                $"{Path.GetFileName(assemblyPath)} is built against Dataverse.Fluent {referenced.ToString(3)}, which is newer than this tool's " +
                $"{tool.ToString(3)}. Update the tool: dotnet tool update --global Dataverse.Fluent.Tool");
        }

        return assembly;
    }

    /// <summary>The models of <paramref name="assembly"/>, optionally narrowed to the named types.</summary>
    public static IReadOnlyList<Type> FindModels(Assembly assembly, IReadOnlyList<string> names)
    {
        var models = DataverseSchemaValidator.FindModels(assembly);
        if (names.Count == 0)
            return models;

        var chosen = new List<Type>();
        foreach (var name in names)
        {
            var matches = models.Where(t => string.Equals(t.FullName, name, StringComparison.Ordinal)
                || string.Equals(t.FullName?.Replace('+', '.'), name, StringComparison.Ordinal)
                || string.Equals(t.Name, name, StringComparison.Ordinal)).ToList();
            if (matches.Count == 0)
                throw new ToolException($"No model named '{name}' in {assembly.GetName().Name}.");
            if (matches.Count > 1)
                throw new ToolException($"'{name}' matches {string.Join(", ", matches.Select(m => m.FullName))}; use the full name.");
            chosen.AddRange(matches);
        }

        return chosen.Distinct().ToList();
    }

    /// <summary>Creates the named <see cref="IDataverseNameResolver"/> from the project and registers it.</summary>
    public static void RegisterResolver(Assembly assembly, string typeName)
    {
        var type = assembly.GetType(typeName)
            ?? GetLoadContext(assembly)?.Assemblies.Select(a => a.GetType(typeName)).FirstOrDefault(t => t is not null)
            ?? throw new ToolException($"No type named '{typeName}' in {assembly.GetName().Name} or its dependencies.");

        if (!typeof(IDataverseNameResolver).IsAssignableFrom(type))
            throw new ToolException($"{typeName} does not implement IDataverseNameResolver.");

        IDataverseNameResolver resolver;
        try
        {
            resolver = (IDataverseNameResolver)Activator.CreateInstance(type)!;
        }
        catch (MissingMethodException ex)
        {
            throw new ToolException($"{typeName} needs a public parameterless constructor to be created by the tool.", ex);
        }

        DataverseTypeMapping.RegisterResolver(resolver);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is { } name && Shared.Contains(name))
            return null;

        return _resolver.ResolveAssemblyToPath(assemblyName) is { } path ? LoadFromBytes(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        => _resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;

    private Assembly LoadFromBytes(string path)
    {
        using var assembly = new MemoryStream(File.ReadAllBytes(path));
        var pdbPath = Path.ChangeExtension(path, ".pdb");
        if (!File.Exists(pdbPath))
            return LoadFromStream(assembly);

        using var pdb = new MemoryStream(File.ReadAllBytes(pdbPath));
        return LoadFromStream(assembly, pdb);
    }
}
