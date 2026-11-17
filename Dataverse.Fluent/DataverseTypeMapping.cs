using System.Reflection;

namespace Dataverse.Fluent;

/// <summary>
/// Global registration point for custom <see cref="IDataverseNameResolver"/> strategies.
/// </summary>
/// <remarks>
/// <para>
/// Register custom resolvers once at application startup. They are consulted in registration
/// order; the built-in <see cref="AttributeNameResolver"/> is always applied last so the
/// library's attributes remain a valid fallback. Each resolver may return <see langword="null"/>
/// to defer to the next resolver in the chain.
/// </para>
/// <para>
/// This enables using POCOs from an external source generator without applying the library's
/// own attributes — register a resolver that understands the generator's conventions.
/// </para>
/// <para>
/// Registration mutates global state and clears the internal name-resolution caches, so it
/// should be performed during startup before the client is used concurrently.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Map types generated with [AttributeLogicalName] / [EntityLogicalName]-style attributes.
/// DataverseTypeMapping.RegisterResolver(new MyGeneratedTypeResolver());
/// </code>
/// </example>
public static class DataverseTypeMapping
{
    private static readonly object _sync = new();
    private static IReadOnlyList<IDataverseNameResolver> _customResolvers = [];

    /// <summary>
    /// Raised whenever the set of registered resolvers changes. Consumed internally to
    /// invalidate cached name-resolution results.
    /// </summary>
    internal static event Action? ResolversChanged;

    /// <summary>
    /// The currently registered custom resolvers, in the order they will be consulted.
    /// The built-in <see cref="AttributeNameResolver"/> is applied after these.
    /// </summary>
    internal static IReadOnlyList<IDataverseNameResolver> CustomResolvers => _customResolvers;

    /// <summary>
    /// Registers a custom resolver, appending it to the end of the resolver chain.
    /// </summary>
    /// <param name="resolver">The resolver to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolver"/> is null.</exception>
    public static void RegisterResolver(IDataverseNameResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        lock (_sync)
        {
            _customResolvers = [.. _customResolvers, resolver];
        }

        ResolversChanged?.Invoke();
    }

    /// <summary>
    /// Removes all registered custom resolvers, restoring the default attribute-based behavior.
    /// Primarily intended for test isolation.
    /// </summary>
    public static void ClearResolvers()
    {
        lock (_sync)
        {
            if (_customResolvers.Count == 0)
            {
                return;
            }

            _customResolvers = [];
        }

        ResolversChanged?.Invoke();
    }

    /// <summary>
    /// Resolves a property name by consulting custom resolvers in order, then the built-in
    /// attribute resolver. Returns <see langword="null"/> when none can resolve the member.
    /// </summary>
    internal static string? ResolvePropertyName(MemberInfo member)
    {
        var resolvers = _customResolvers;
        for (var i = 0; i < resolvers.Count; i++)
        {
            var name = resolvers[i].ResolvePropertyName(member);
            if (name != null)
            {
                return name;
            }
        }

        return AttributeNameResolver.Instance.ResolvePropertyName(member);
    }

    /// <summary>
    /// Resolves an entity set name by consulting custom resolvers in order, then the built-in
    /// attribute resolver. Returns <see langword="null"/> when none can resolve the type.
    /// </summary>
    internal static string? ResolveEntitySetName(Type type)
    {
        var resolvers = _customResolvers;
        for (var i = 0; i < resolvers.Count; i++)
        {
            var name = resolvers[i].ResolveEntitySetName(type);
            if (name != null)
            {
                return name;
            }
        }

        return AttributeNameResolver.Instance.ResolveEntitySetName(type);
    }

    /// <summary>
    /// Resolves a table's logical name by consulting custom resolvers in order, then the built-in
    /// attribute resolver. Returns <see langword="null"/> when none can resolve the type.
    /// </summary>
    internal static string? ResolveEntityLogicalName(Type type)
    {
        var resolvers = _customResolvers;
        for (var i = 0; i < resolvers.Count; i++)
        {
            var name = resolvers[i].ResolveEntityLogicalName(type);
            if (name != null)
            {
                return name;
            }
        }

        return AttributeNameResolver.Instance.ResolveEntityLogicalName(type);
    }
}
