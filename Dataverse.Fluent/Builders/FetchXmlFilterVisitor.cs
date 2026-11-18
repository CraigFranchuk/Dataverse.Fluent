using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using Dataverse.Fluent.Builders.Expressions;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Visitor that converts lambda expressions to FetchXML filter conditions.
/// </summary>
internal class FetchXmlFilterVisitor : ExpressionVisitor
{
    private readonly Stack<Operand> _operandStack = new();

    // An attribute name, an XML-escaped value, or a finished <condition>/<filter> element. IsNull marks
    // the null literal, which is distinct from a string whose text happens to be "null".
    private readonly record struct Operand(string Text, bool IsNull = false);

    public string ToFetchXmlFilter(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        // Evaluate everything that does not depend on the lambda parameter first, so only attributes,
        // values and operators remain to be translated.
        Visit(PartialEvaluator.Evaluate(expression));

        return _operandStack.Count > 0 ? _operandStack.Pop().Text : string.Empty;
    }

    /// <summary>
    /// Rejects every node type the translator does not handle, so an unsupported expression fails
    /// instead of producing a wrong filter.
    /// </summary>
    public override Expression? Visit(Expression? node)
    {
        if (node is null)
            return null;

        return node.NodeType switch
        {
            ExpressionType.Lambda or ExpressionType.MemberAccess or ExpressionType.Constant
                or ExpressionType.Call or ExpressionType.Convert or ExpressionType.ConvertChecked
                or ExpressionType.Not
                or ExpressionType.Equal or ExpressionType.NotEqual
                or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
                or ExpressionType.LessThan or ExpressionType.LessThanOrEqual
                or ExpressionType.AndAlso or ExpressionType.OrElse
                => base.Visit(node),
            _ => throw new NotSupportedException(
                $"Expression '{node}' ({node.NodeType}) is not supported in FetchXML filters.")
        };
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        Visit(node.Left);
        var left = _operandStack.Pop();

        Visit(node.Right);
        var right = _operandStack.Pop();

        if (node.NodeType == ExpressionType.AndAlso || node.NodeType == ExpressionType.OrElse)
        {
            var filterType = node.NodeType == ExpressionType.AndAlso ? "and" : "or";
            _operandStack.Push(new Operand($"<filter type=\"{filterType}\">{left.Text}{right.Text}</filter>"));
            return node;
        }

        if (right.IsNull)
        {
            var nullOperator = node.NodeType switch
            {
                ExpressionType.Equal => "null",
                ExpressionType.NotEqual => "not-null",
                _ => throw new NotSupportedException(
                    $"Binary operator '{node.NodeType}' cannot compare with null in FetchXML filters.")
            };

            _operandStack.Push(new Operand($"<condition attribute=\"{left.Text}\" operator=\"{nullOperator}\" />"));
            return node;
        }

        var fetchOperator = node.NodeType switch
        {
            ExpressionType.Equal => "eq",
            ExpressionType.NotEqual => "ne",
            ExpressionType.GreaterThan => "gt",
            ExpressionType.GreaterThanOrEqual => "ge",
            ExpressionType.LessThan => "lt",
            ExpressionType.LessThanOrEqual => "le",
            _ => throw new NotSupportedException($"Binary operator '{node.NodeType}' is not supported in FetchXML filters.")
        };

        _operandStack.Push(new Operand(
            $"<condition attribute=\"{left.Text}\" operator=\"{fetchOperator}\" value=\"{right.Text}\" />"));
        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        // Nullable<T>.HasValue → not-null condition
        if (node.Member.Name == "HasValue" && IsNullableType(node.Member.DeclaringType))
        {
            Visit(node.Expression!);
            var attribute = _operandStack.Pop();
            _operandStack.Push(new Operand($"<condition attribute=\"{attribute.Text}\" operator=\"not-null\" />"));
            return node;
        }

        // Nullable<T>.Value → the attribute itself
        if (node.Member.Name == "Value" && IsNullableType(node.Member.DeclaringType))
        {
            Visit(node.Expression!);
            return node;
        }

        if (node.Expression?.NodeType == ExpressionType.Parameter)
        {
            _operandStack.Push(new Operand(EntityTypeMetadata.GetLogicalName(node.Member)));
            return node;
        }

        // A polymorphic lookup's Id (x.CustomerId.Id) is the lookup attribute itself.
        if (node.Member.DeclaringType == typeof(DataverseReference) && node.Member.Name == nameof(DataverseReference.Id)
            && node.Expression is MemberExpression { Expression.NodeType: ExpressionType.Parameter } reference)
        {
            _operandStack.Push(new Operand(EntityTypeMetadata.GetLogicalName(reference.Member)));
            return node;
        }

        throw new NotSupportedException($"Member access '{node}' is not supported in FetchXML filters.");
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        _operandStack.Push(FormatValue(node.Value));
        return node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.DeclaringType == typeof(string))
        {
            return VisitStringMethod(node);
        }

        // Enumerable.Contains(source, item) or MemoryExtensions.Contains — static extension methods
        if (node.Method.Name == "Contains" && node.Method.IsStatic && node.Arguments.Count >= 2
            && node.Method.DeclaringType != typeof(string))
        {
            return VisitEnumerableContains(node.Arguments[0], node.Arguments[1]);
        }

        // List<T>.Contains(item), ICollection<T>.Contains(item) — instance method
        if (node.Method.Name == "Contains" && node.Object != null && node.Arguments.Count == 1 && IsCollectionType(node.Object.Type))
        {
            return VisitEnumerableContains(node.Object, node.Arguments[0]);
        }

        throw new NotSupportedException($"Method '{node.Method.Name}' is not supported in FetchXML filters.");
    }

    private MethodCallExpression VisitStringMethod(MethodCallExpression node)
    {
        if (node.Object is null)
            throw new NotSupportedException($"Static string method '{node.Method.Name}' is not supported in FetchXML filters.");

        var pattern = node.Method.Name switch
        {
            "Contains" => "%{0}%",
            "StartsWith" => "{0}%",
            "EndsWith" => "%{0}",
            _ => throw new NotSupportedException($"String method '{node.Method.Name}' is not supported in FetchXML filters.")
        };

        Visit(node.Object);
        var target = _operandStack.Pop();

        Visit(node.Arguments[0]);
        var value = _operandStack.Pop();

        _operandStack.Push(new Operand(
            $"<condition attribute=\"{target.Text}\" operator=\"like\" value=\"{string.Format(CultureInfo.InvariantCulture, pattern, value.Text)}\" />"));
        return node;
    }

    private Expression VisitEnumerableContains(Expression source, Expression item)
    {
        // On .NET 10+ the source may be wrapped in ReadOnlySpan<T>.op_Implicit, which EvaluateValue unwraps.
        var collection = PartialEvaluator.EvaluateValue(source);
        if (collection is not IEnumerable enumerable)
        {
            throw new NotSupportedException("The source of Contains() must be an IEnumerable.");
        }

        var values = new List<string>();
        foreach (var val in enumerable)
        {
            values.Add(FormatValue(val).Text);
        }

        if (values.Count == 0)
        {
            throw new ArgumentException("The collection used in Contains() must have at least one element.");
        }

        Visit(item);
        var property = _operandStack.Pop();

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<condition attribute=\"{property.Text}\" operator=\"in\">");
        foreach (var v in values)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<value>{v}</value>");
        }
        sb.Append("</condition>");

        _operandStack.Push(new Operand(sb.ToString()));
        return source;
    }

    private static bool IsCollectionType(Type type)
    {
        if (type == typeof(string))
            return false;

        return type.IsArray
            || type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
    }

    private static bool IsNullableType(Type? type)
        => type is { IsGenericType: true } && type.GetGenericTypeDefinition() == typeof(Nullable<>);

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Not)
        {
            Visit(node.Operand);
            var operand = _operandStack.Pop();

            _operandStack.Push(new Operand(NegateFilter(operand.Text)));
            return node;
        }

        // Convert / ConvertChecked: the only other unary nodes Visit lets through.
        return Visit(node.Operand)!;
    }

    protected override Expression VisitLambda<TDelegate>(Expression<TDelegate> node)
    {
        Visit(node.Body);
        return node;
    }

    // De Morgan's law: NOT(a AND b) → (NOT a) OR (NOT b)
    // Swaps filter type (and↔or) and negates each condition operator.
    private static readonly Dictionary<string, string> NegatedOperators = new()
    {
        ["eq"] = "ne",
        ["ne"] = "eq",
        ["gt"] = "le",
        ["ge"] = "lt",
        ["lt"] = "ge",
        ["le"] = "gt",
        ["like"] = "not-like",
        ["not-like"] = "like",
        ["null"] = "not-null",
        ["not-null"] = "null",
        ["in"] = "not-in",
        ["not-in"] = "in",
    };

    private static string NegateFilter(string xml)
    {
        if (xml.Contains("<filter"))
        {
            // Swap filter type: and↔or
            xml = xml
                .Replace("type=\"and\"", "type=\"__placeholder__\"")
                .Replace("type=\"or\"", "type=\"and\"")
                .Replace("type=\"__placeholder__\"", "type=\"or\"");
        }

        // Negate each condition operator
        foreach (var kvp in NegatedOperators)
        {
            xml = xml.Replace($"operator=\"{kvp.Key}\"", $"operator=\"__{kvp.Key}__\"");
        }

        foreach (var kvp in NegatedOperators)
        {
            xml = xml.Replace($"operator=\"__{kvp.Key}__\"", $"operator=\"{kvp.Value}\"");
        }

        return xml;
    }

    private static Operand FormatValue(object? value)
    {
        if (value is null)
            return new Operand("null", IsNull: true);

        var text = value switch
        {
            // Choice (option set) values are matched by their integer value.
            Enum e => Convert.ToString(
                Convert.ChangeType(e, Enum.GetUnderlyingType(e.GetType()), CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture)!,
            string str => str,
            bool b => b ? "1" : "0",
            Guid g => g.ToString(),
            // UTC ISO 8601, as in OData filters: a zone-less value would be read in the calling user's time zone.
            DateTime dt => ODataLiteral.FormatDateTime(dt),
            DateTimeOffset dto => ODataLiteral.FormatDateTime(dto),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        return new Operand(EscapeXml(text));
    }

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
