using System.Reflection;

namespace Dataverse.Fluent;

/// <summary>
/// Resolves Dataverse logical names for POCO members and entity set names for POCO types.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface to make the client work with POCOs produced by an external
/// source generator (or any other convention) without applying the library's own
/// <see cref="DataverseColumnAttribute"/>/<see cref="EntitySetAttribute"/> attributes.
/// </para>
/// <para>
/// Resolvers are consulted in registration order via <see cref="DataverseTypeMapping"/>.
/// Return <see langword="null"/> from any method to defer resolution to the next resolver
/// in the chain (and ultimately to the built-in attribute-based defaults). This means a
/// resolver can handle only the types or members it understands and ignore the rest.
/// </para>
/// <para>
/// Implementations must be thread-safe: a single instance can be invoked concurrently from
/// multiple threads. Results are cached by the library, so resolvers should return stable
/// values for a given input.
/// </para>
/// </remarks>
public interface IDataverseNameResolver
{
    /// <summary>
    /// Resolves the Dataverse logical (column) name for a POCO member.
    /// </summary>
    /// <param name="member">The property or field being mapped.</param>
    /// <returns>
    /// The Dataverse logical name, or <see langword="null"/> to defer to the next resolver.
    /// </returns>
    string? ResolvePropertyName(MemberInfo member);

    /// <summary>
    /// Resolves the OData entity set name for a POCO type.
    /// </summary>
    /// <param name="type">The entity type being mapped.</param>
    /// <returns>
    /// The OData entity set name (e.g., "accounts"), or <see langword="null"/> to defer to
    /// the next resolver.
    /// </returns>
    string? ResolveEntitySetName(Type type);

    /// <summary>
    /// Resolves the Dataverse logical name of the table a POCO type maps to (used by FetchXML).
    /// </summary>
    /// <param name="type">The entity type being mapped.</param>
    /// <returns>
    /// The table's logical name (e.g., "account"), or <see langword="null"/> to defer to the next resolver.
    /// The default implementation defers.
    /// </returns>
    string? ResolveEntityLogicalName(Type type) => null;
}
