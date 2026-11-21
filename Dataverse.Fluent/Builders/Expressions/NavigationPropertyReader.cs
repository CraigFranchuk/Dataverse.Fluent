using System.Linq.Expressions;

namespace Dataverse.Fluent.Builders.Expressions;

/// <summary>
/// Reads the navigation property named by a selector (<c>x =&gt; x.Contacts</c>), so that <c>Expand</c>,
/// <c>WithRelated</c> and <c>Via</c> take the relationship from the model instead of a string.
/// </summary>
internal static class NavigationPropertyReader
{
    /// <summary>
    /// Returns the name the navigation property is read under, and whether it holds a collection.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The selector is not a direct property of the entity, or the property holds a column or a lookup value
    /// rather than related records.
    /// </exception>
    public static (string Name, bool IsCollection) Read<T>(LambdaExpression navigation)
    {
        var property = MemberPathExtractor.GetProperty(navigation);
        var member = EntityTypeMetadata.For(typeof(T)).Find(property);
        var (related, isCollection) = EntityTypeMetadata.RelatedRecordType(property.PropertyType);

        // A property typed as a record, or a collection of records, holds related records even when the related
        // type has no entity set mapped; a column, a lookup or an ignored property does not.
        if (member is null || related is null || member.Kind is not (EntityMemberKind.Navigation or EntityMemberKind.Column))
        {
            throw new ArgumentException(
                $"{typeof(T).Name}.{property.Name} is not a navigation property: it must hold a related record or a collection " +
                "of related records. To use a relationship that is not on the model, pass its name as a string.",
                nameof(navigation));
        }

        return (member.Name, isCollection);
    }
}
