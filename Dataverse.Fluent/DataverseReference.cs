namespace Dataverse.Fluent;

/// <summary>
/// The value of a polymorphic lookup, such as customer, owner or regarding: the table the lookup points at, and the
/// record's ID.
/// </summary>
/// <remarks>
/// <para>
/// Use it as the type of a property marked with <see cref="DataverseLookupAttribute(string)"/> and one
/// <see cref="DataverseLookupTargetAttribute"/> per table the lookup can point at. Reads fill both parts, and
/// <c>SetFrom</c>, <c>Set</c>, batches and bulk operations write the bind for the matching target.
/// </para>
/// <para>
/// <see cref="LogicalName"/> is <see langword="null"/> when a response did not say which table the lookup points at,
/// for example a raw request sent without the <c>Microsoft.Dynamics.CRM.lookuplogicalname</c> annotation. Such a
/// reference cannot be written.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DataverseLookup("customerid")]
/// [DataverseLookupTarget("account", "accounts", "customerid_account")]
/// [DataverseLookupTarget("contact", "contacts", "customerid_contact")]
/// public DataverseReference? CustomerId { get; set; }
///
/// opportunity.CustomerId = DataverseReference.To&lt;Account&gt;(accountId);
/// </code>
/// </example>
public sealed record DataverseReference
{
    /// <summary>
    /// Initializes a reference to the record <paramref name="id"/> in the table <paramref name="logicalName"/>.
    /// </summary>
    /// <param name="logicalName">The table's logical name, e.g. <c>account</c>.</param>
    /// <param name="id">The record's ID.</param>
    /// <exception cref="ArgumentException"><paramref name="logicalName"/> is empty, or <paramref name="id"/> is
    /// <see cref="Guid.Empty"/>.</exception>
    public DataverseReference(string logicalName, Guid id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
        if (id == Guid.Empty)
            throw new ArgumentException("A reference needs a record ID.", nameof(id));

        LogicalName = logicalName.ToLowerInvariant();
        Id = id;
    }

    // As read from a response, where either part may arrive first, or the table may be missing.
    private DataverseReference()
    {
    }

    /// <summary>
    /// The logical name of the table the lookup points at, e.g. <c>account</c>, or <see langword="null"/> when the
    /// response did not include it.
    /// </summary>
    public string? LogicalName { get; private init; }

    /// <summary>The ID of the record the lookup points at.</summary>
    public Guid Id { get; private init; }

    /// <summary>
    /// Creates a reference to a record of the table mapped to <typeparamref name="T"/>, whose logical name comes
    /// from <see cref="DataverseEntityAttribute"/> or a registered <see cref="IDataverseNameResolver"/>.
    /// </summary>
    /// <typeparam name="T">The POCO type of the target table.</typeparam>
    /// <param name="id">The record's ID.</param>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> has no logical name mapping.</exception>
    public static DataverseReference To<T>(Guid id)
    {
        var logicalName = DataverseTypeMapping.ResolveEntityLogicalName(typeof(T))
            ?? throw new InvalidOperationException(
                $"Type '{typeof(T).Name}' has no logical name. Apply [DataverseEntity(\"logicalname\", \"entityset\")], " +
                "register an IDataverseNameResolver, or use new DataverseReference(\"logicalname\", id).");
        return new DataverseReference(logicalName, id);
    }

    // A response carries the ID and the table as two properties, in either order.
    internal static DataverseReference WithId(DataverseReference? current, Guid id)
        => new() { LogicalName = current?.LogicalName, Id = id };

    internal static DataverseReference WithLogicalName(DataverseReference? current, string? logicalName)
        => new() { LogicalName = logicalName?.ToLowerInvariant(), Id = current?.Id ?? Guid.Empty };

    /// <summary>Returns <c>logicalname(id)</c>.</summary>
    public override string ToString() => $"{LogicalName ?? "?"}({Id})";
}
