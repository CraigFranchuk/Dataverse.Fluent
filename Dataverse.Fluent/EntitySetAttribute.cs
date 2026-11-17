namespace Dataverse.Fluent;

/// <summary>
/// Specifies the OData entity set name for a POCO type.
/// Generic entry points such as <c>Entity&lt;T&gt;()</c> and <c>Query&lt;T&gt;()</c> use this name; without it (or a
/// registered <see cref="IDataverseNameResolver"/>) the entity set name must be passed explicitly.
/// </summary>
/// <example>
/// <code>
/// [EntitySet("opportunities")]
/// public class Opportunity
/// {
///     [JsonPropertyName("opportunityid")]
///     public Guid OpportunityId { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class EntitySetAttribute : Attribute
{
    /// <summary>
    /// The OData entity set name (e.g., "opportunities", "businessunits").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="EntitySetAttribute"/> with the specified entity set name.
    /// </summary>
    /// <param name="name">The OData entity set name.</param>
    public EntitySetAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }
}
