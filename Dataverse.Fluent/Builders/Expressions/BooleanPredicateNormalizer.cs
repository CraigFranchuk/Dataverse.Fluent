using System.Linq.Expressions;

namespace Dataverse.Fluent.Builders.Expressions;

/// <summary>
/// Rewrites a boolean column used on its own as a condition (<c>x =&gt; x.IsActive</c>, <c>!x.IsActive</c>,
/// <c>x.Age &gt; 1 &amp;&amp; x.IsActive</c>) into an explicit comparison with <see langword="true"/>, so the translators
/// only ever see comparisons.
/// </summary>
/// <remarks>
/// Dataverse accepts a bare column in <c>$filter</c> (<c>$filter=creditonhold</c>) and in a FetchXML <c>filter</c>,
/// but ignores it: both returned every row, records with the flag set and without it (checked 2026-11-20). Only
/// <c>creditonhold eq true</c> and <c>&lt;condition operator="eq" value="1"&gt;</c> filter. Run this after
/// <see cref="PartialEvaluator"/>, so a member left in the tree is a column, not a captured variable.
/// </remarks>
internal sealed class BooleanPredicateNormalizer : ExpressionVisitor
{
    private static readonly ConstantExpression True = Expression.Constant(true);

    public static Expression Normalize(Expression expression)
        => new BooleanPredicateNormalizer().Visit(expression);

    protected override Expression VisitLambda<T>(Expression<T> node)
        => node.Update(AsCondition(Visit(node.Body)), node.Parameters);

    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse)
            return node.Update(AsCondition(Visit(node.Left)), node.Conversion, AsCondition(Visit(node.Right)));

        return base.VisitBinary(node);
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Not && node.Type == typeof(bool))
            return node.Update(AsCondition(Visit(node.Operand)));

        return base.VisitUnary(node);
    }

    // A bool member in the position of a condition: a column (x.Flag), a nullable one's value (x.Flag.Value) or a
    // related record's column (x.Parent.Flag). Nullable<T>.HasValue is a condition already ("x ne null").
    private static Expression AsCondition(Expression expression)
        => expression is MemberExpression member && member.Type == typeof(bool) && !IsHasValue(member)
            ? Expression.Equal(member, True)
            : expression;

    private static bool IsHasValue(MemberExpression member)
        => member.Member.Name == "HasValue" && member.Member.DeclaringType is { IsGenericType: true } type
           && type.GetGenericTypeDefinition() == typeof(Nullable<>);
}
