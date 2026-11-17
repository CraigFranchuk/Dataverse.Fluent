using System.Linq.Expressions;
using System.Reflection;

namespace Dataverse.Fluent.Builders.Expressions;

/// <summary>
/// Reads the members named by a property selector (<c>x =&gt; x.Name</c>) or a projection
/// (<c>x =&gt; new { x.Name, x.Revenue }</c>, <c>x =&gt; new Dto { Name = x.Name }</c>).
/// </summary>
/// <remarks>
/// Only direct members of the lambda parameter are accepted. Anything else, such as a computed value
/// (<c>x.Name.ToUpper()</c>) or a member of a related record (<c>x.PrimaryContact.FullName</c>), throws
/// rather than being dropped or mapped to the wrong column.
/// </remarks>
internal static class MemberPathExtractor
{
    /// <summary>
    /// Returns the OData property names selected by a single-member or projection lambda.
    /// </summary>
    public static List<string> GetPropertyNames(LambdaExpression selector)
        => GetMembers(selector).ConvertAll(ODataPropertyNameCache.GetODataPropertyName);

    /// <summary>
    /// Returns the OData property name of a single-property lambda (<c>x =&gt; x.Name</c>).
    /// </summary>
    public static string GetPropertyName(LambdaExpression selector)
        => ODataPropertyNameCache.GetODataPropertyName(GetProperty(selector));

    /// <summary>
    /// Returns the property named by a single-property lambda (<c>x =&gt; x.Name</c>).
    /// </summary>
    public static PropertyInfo GetProperty(LambdaExpression selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        if (TryGetDirectMember(selector.Body, out var member))
        {
            return member as PropertyInfo
                ?? throw new ArgumentException("Expression must reference a property", nameof(selector));
        }

        throw new ArgumentException(
            "Expression must be a simple property accessor (e.g., x => x.PropertyName)", nameof(selector));
    }

    /// <summary>
    /// Returns the members selected by a single-member or projection lambda, in the order written.
    /// </summary>
    public static List<MemberInfo> GetMembers(LambdaExpression selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var members = new List<MemberInfo>();
        var body = StripConversions(selector.Body);

        switch (body)
        {
            case NewExpression created:
                AddArguments(created, members);
                break;

            case MemberInitExpression initializer:
                AddArguments(initializer.NewExpression, members);
                foreach (var binding in initializer.Bindings)
                {
                    if (binding is not MemberAssignment assignment)
                        throw Unsupported(selector, binding.Member.Name);

                    members.Add(GetDirectMember(assignment.Expression, selector));
                }
                break;

            default:
                members.Add(GetDirectMember(body, selector));
                break;
        }

        return members;

        void AddArguments(NewExpression created, List<MemberInfo> target)
        {
            foreach (var argument in created.Arguments)
                target.Add(GetDirectMember(argument, selector));
        }
    }

    private static MemberInfo GetDirectMember(Expression expression, LambdaExpression selector)
        => TryGetDirectMember(expression, out var member) ? member : throw Unsupported(selector, expression.ToString());

    private static bool TryGetDirectMember(Expression expression, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MemberInfo? member)
    {
        expression = StripConversions(expression);

        // x.Revenue.Value on a nullable column selects the column itself.
        if (expression is MemberExpression { Member.Name: "Value", Expression: { } nullable }
            && Nullable.GetUnderlyingType(nullable.Type) != null)
        {
            expression = StripConversions(nullable);
        }

        if (expression is MemberExpression { Expression: { } owner } access
            && StripConversions(owner) is ParameterExpression)
        {
            member = access.Member;
            return true;
        }

        member = null;
        return false;
    }

    private static Expression StripConversions(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
            expression = convert.Operand;
        return expression;
    }

    private static ArgumentException Unsupported(LambdaExpression selector, string part)
        => new(
            $"'{part}' in '{selector}' is not a property of the entity. Select properties directly " +
            "(e.g., x => x.Name or x => new { x.Name, x.Revenue }); computed values are not supported, and " +
            "properties of a related record are selected with Expand().",
            nameof(selector));
}
