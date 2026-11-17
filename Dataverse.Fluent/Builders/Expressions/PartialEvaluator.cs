using System.Linq.Expressions;
using System.Reflection;

namespace Dataverse.Fluent.Builders.Expressions;

/// <summary>
/// Replaces every subtree of an expression that does not depend on a lambda parameter with a constant
/// holding its value, so the translators only ever see columns, literals and operators.
/// <c>x =&gt; x.CreatedOn &gt; DateTime.UtcNow.AddDays(-7)</c> becomes <c>x =&gt; x.CreatedOn &gt; [constant]</c>.
/// </summary>
internal static class PartialEvaluator
{
    /// <summary>
    /// Returns <paramref name="expression"/> with its parameter-independent subtrees evaluated.
    /// </summary>
    public static Expression Evaluate(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var candidates = new Nominator().Nominate(expression);
        return candidates.Count == 0 ? expression : new SubtreeEvaluator(candidates).Visit(expression)!;
    }

    /// <summary>
    /// Evaluates an expression that does not depend on a lambda parameter. Constants, closure and static
    /// member chains, conversions and array initializers are read by walking the tree; any other shape is
    /// compiled.
    /// </summary>
    public static object? EvaluateValue(Expression expression)
    {
        switch (expression)
        {
            case ConstantExpression constant:
                return constant.Value;

            case MemberExpression member when TryEvaluateMemberChain(member, out var value):
                return value;

            case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert
                when IsIdentityLikeConversion(convert):
                return EvaluateValue(convert.Operand);

            case NewArrayExpression { NodeType: ExpressionType.NewArrayInit } newArray:
                var array = Array.CreateInstance(newArray.Type.GetElementType()!, newArray.Expressions.Count);
                for (var i = 0; i < newArray.Expressions.Count; i++)
                    array.SetValue(EvaluateValue(newArray.Expressions[i]), i);
                return array;

            // On .NET 10+, MemoryExtensions.Contains wraps the source array in ReadOnlySpan<T>.op_Implicit(T[]).
            // A span cannot be boxed, so the wrapped collection is returned instead.
            case MethodCallExpression { Method.Name: "op_Implicit", Arguments.Count: 1 } spanConversion
                when spanConversion.Type.IsByRefLike:
                return EvaluateValue(spanConversion.Arguments[0]);

            default:
                return Compile(expression);
        }
    }

    // Boxing, unboxing-free reference conversions and nullable wrapping keep the boxed value as it is.
    // Numeric and user-defined conversions change the value and are compiled.
    private static bool IsIdentityLikeConversion(UnaryExpression convert)
    {
        if (convert.Method != null)
            return false;

        var from = Nullable.GetUnderlyingType(convert.Operand.Type) ?? convert.Operand.Type;
        var to = Nullable.GetUnderlyingType(convert.Type) ?? convert.Type;
        return from == to || (!from.IsValueType && !to.IsValueType) || (from.IsValueType && to == typeof(object));
    }

    private static bool TryEvaluateMemberChain(MemberExpression node, out object? value)
    {
        var chain = new Stack<MemberInfo>();
        Expression? current = node;
        while (current is MemberExpression member)
        {
            chain.Push(member.Member);
            current = member.Expression;
        }

        // A null root is a static member; otherwise the root must be a constant (a closure instance).
        if (current is not (null or ConstantExpression))
        {
            value = null;
            return false;
        }

        value = (current as ConstantExpression)?.Value;
        while (chain.Count > 0)
        {
            value = chain.Pop() switch
            {
                FieldInfo field => field.GetValue(value),
                PropertyInfo property => property.GetValue(value),
                var other => throw new NotSupportedException($"Member type '{other.MemberType}' is not supported.")
            };
        }

        return true;
    }

    private static object? Compile(Expression expression)
    {
        try
        {
            return Expression.Lambda(expression).Compile(preferInterpretation: true).DynamicInvoke();
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static bool CanBeEvaluated(Expression expression)
    {
        if (expression.NodeType is ExpressionType.Parameter or ExpressionType.Lambda or ExpressionType.Quote)
            return false;

        // By-ref-like values (spans) cannot be boxed into a constant.
        if (expression.Type == typeof(void) || expression.Type.IsByRefLike)
            return false;

        // Query-function markers are translated, never invoked.
        return expression is not MethodCallExpression call || call.Method.DeclaringType != typeof(DataverseFunctions);
    }

    // Finds the maximal subtrees that do not reference a parameter.
    private sealed class Nominator : ExpressionVisitor
    {
        private readonly HashSet<Expression> _candidates = [];
        private bool _cannotBeEvaluated;

        public HashSet<Expression> Nominate(Expression expression)
        {
            Visit(expression);
            return _candidates;
        }

        public override Expression? Visit(Expression? node)
        {
            if (node is null)
                return null;

            var saved = _cannotBeEvaluated;
            _cannotBeEvaluated = false;
            base.Visit(node);

            if (!_cannotBeEvaluated)
            {
                if (CanBeEvaluated(node))
                    _candidates.Add(node);
                else
                    _cannotBeEvaluated = true;
            }

            _cannotBeEvaluated |= saved;
            return node;
        }
    }

    private sealed class SubtreeEvaluator(HashSet<Expression> candidates) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node)
        {
            if (node is null)
                return null;

            if (!candidates.Contains(node))
                return base.Visit(node);

            return node is ConstantExpression ? node : Expression.Constant(EvaluateValue(node), node.Type);
        }
    }
}
