using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Dataverse.Fluent.Builders.Expressions;

namespace Dataverse.Fluent.Builders;

internal class ODataExpressionVisitor : ExpressionVisitor
{
    private readonly StringBuilder _filter = new();
    private readonly Stack<string> _operandStack = new();

    public string ToODataFilter(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        
        // Evaluate everything that does not depend on the lambda parameter first, so only columns,
        // literals and operators remain to be translated.
        Visit(PartialEvaluator.Evaluate(expression));
        return _filter.ToString();
    }

    /// <summary>
    /// Rejects every node type the translator does not handle. Without this, the base visitor would walk
    /// an unsupported node's children and leave their operands on the stack, producing a wrong filter
    /// instead of an error.
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
                or ExpressionType.Add or ExpressionType.Subtract or ExpressionType.Multiply
                or ExpressionType.Divide or ExpressionType.Modulo
                => base.Visit(node),
            _ => throw new NotSupportedException(
                $"Expression '{node}' ({node.NodeType}) is not supported in OData filters.")
        };
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        // Dataverse rejects every OData arithmetic operator (HTTP 501), so an expression that needs one has no
        // translation. Values that do not depend on the record were already computed on the client.
        if (node.NodeType is ExpressionType.Add or ExpressionType.AddChecked or ExpressionType.Subtract
            or ExpressionType.SubtractChecked or ExpressionType.Multiply or ExpressionType.MultiplyChecked
            or ExpressionType.Divide or ExpressionType.Modulo)
        {
            throw new NotSupportedException(
                $"'{node}' cannot be filtered: Dataverse does not support arithmetic in $filter. Compare the column with a value " +
                "computed in C# instead (e.g., x => x.Revenue > limit - 1), or filter the results after reading them.");
        }

        var precedence = GetPrecedence(node.NodeType);

        Visit(node.Left);
        var left = _operandStack.Pop();
        if (NeedsParentheses(node.Left, precedence, isRightOperand: false, node.NodeType))
            left = $"({left})";

        Visit(node.Right);
        var right = _operandStack.Pop();
        if (NeedsParentheses(node.Right, precedence, isRightOperand: true, node.NodeType))
            right = $"({right})";

        var odataOperator = node.NodeType switch
        {
            ExpressionType.Equal => "eq",
            ExpressionType.NotEqual => "ne",
            ExpressionType.GreaterThan => "gt",
            ExpressionType.GreaterThanOrEqual => "ge",
            ExpressionType.LessThan => "lt",
            ExpressionType.LessThanOrEqual => "le",
            ExpressionType.AndAlso => "and",
            ExpressionType.OrElse => "or",
            _ => throw new NotSupportedException($"Binary operator '{node.NodeType}' is not supported in OData filters.")
        };

        var result = $"{left} {odataOperator} {right}";
        _operandStack.Push(result);

        return node;
    }

    // OData operator precedence (higher binds tighter): or < and < comparison < additive < multiplicative.
    // C# expression trees carry grouping structurally, so it must be re-expressed with parentheses
    // whenever a child operator binds more loosely than its parent.
    private static int GetPrecedence(ExpressionType nodeType) => nodeType switch
    {
        ExpressionType.OrElse => 1,
        ExpressionType.AndAlso => 2,
        ExpressionType.Equal or ExpressionType.NotEqual or ExpressionType.GreaterThan
            or ExpressionType.GreaterThanOrEqual or ExpressionType.LessThan
            or ExpressionType.LessThanOrEqual => 3,
        ExpressionType.Add or ExpressionType.Subtract => 4,
        ExpressionType.Multiply or ExpressionType.Divide or ExpressionType.Modulo => 5,
        _ => int.MaxValue
    };

    private static bool NeedsParentheses(Expression operand, int parentPrecedence, bool isRightOperand, ExpressionType parentType)
    {
        while (operand is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
            operand = convert.Operand;

        int childPrecedence;
        if (operand is BinaryExpression binary)
            childPrecedence = GetPrecedence(binary.NodeType);
        else if (operand is MemberExpression { Member.Name: "HasValue" } member && IsNullableType(member.Member.DeclaringType))
            childPrecedence = GetPrecedence(ExpressionType.NotEqual); // emitted as "x ne null"
        else
            return false;

        if (childPrecedence < parentPrecedence)
            return true;

        // Equal precedence: and/or, add and mul are associative, so only the right operand of a
        // non-associative operator (sub, div, mod, comparisons) needs grouping. Comparisons are
        // also grouped on the left because "a gt b eq true" is ambiguous to readers and parsers alike.
        if (childPrecedence == parentPrecedence)
        {
            var associative = parentType is ExpressionType.AndAlso or ExpressionType.OrElse
                or ExpressionType.Add or ExpressionType.Multiply;
            return parentPrecedence == 3 || (isRightOperand && !associative);
        }

        return false;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        // Nullable<T>.HasValue → "propertyName ne null"
        if (node.Member.Name == "HasValue" && IsNullableType(node.Member.DeclaringType))
        {
            Visit(node.Expression!);
            var inner = _operandStack.Pop();
            _operandStack.Push($"{inner} ne null");
            return node;
        }

        // Nullable<T>.Value → unwrap to just the property name
        if (node.Member.Name == "Value" && IsNullableType(node.Member.DeclaringType))
        {
            Visit(node.Expression!);
            return node;
        }

        // Dataverse rejects the OData length() function (HTTP 501).
        if (node.Member.Name == "Length" && node.Member.DeclaringType == typeof(string))
        {
            throw new NotSupportedException(
                $"'{node}' cannot be filtered: Dataverse does not support the length() function in $filter.");
        }

        if (node.Expression?.NodeType == ExpressionType.Parameter)
        {
            var propertyName = GetODataPropertyName(node.Member);
            _operandStack.Push(propertyName);
        }
        else if (TryGetNavigationPath(node, out var navigationPath))
        {
            // Navigation/lookup filter (e.g., x.PrimaryContact.LastName) → "primarycontact/lastname"
            _operandStack.Push(navigationPath);
        }
        else
        {
            throw new NotSupportedException(
                $"Member access '{node}' is not supported in OData filters.");
        }

        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        _operandStack.Push(ODataLiteral.Format(node.Value));
        return node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.DeclaringType == typeof(string))
        {
            return VisitStringMethod(node);
        }

        // Dataverse-specific query functions (Above, Under, EqualUserId, etc.) exposed via DataverseFunctions.
        if (node.Method.DeclaringType == typeof(DataverseFunctions))
        {
            return VisitDataverseFunction(node);
        }

        // Enumerable.Contains(source, item) or MemoryExtensions.Contains(span, item, [comparer]) — static extension methods
        // ArgCount is 2 for primitives (MemoryExtensions.Contains) and 3 for enums
        // (MemoryExtensions.Contains with IEqualityComparer). Item is always Arguments[1].
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

        throw new NotSupportedException($"Method '{node.Method.Name}' is not supported in OData filters.");
    }

    private MethodCallExpression VisitStringMethod(MethodCallExpression node)
    {
        // Static string methods: string.IsNullOrEmpty, string.Concat
        if (node.Method.IsStatic)
        {
            return VisitStaticStringMethod(node);
        }

        // Dataverse supports only contains, startswith and endswith; tolower, toupper, trim, substring, indexof and the
        // other OData string functions are rejected (HTTP 501).
        var function = node.Method.Name switch
        {
            "Contains" => "contains",
            "StartsWith" => "startswith",
            "EndsWith" => "endswith",
            "ToLower" or "ToLowerInvariant" or "ToUpper" or "ToUpperInvariant" => throw new NotSupportedException(
                $"'{node}' cannot be filtered: Dataverse does not support {node.Method.Name.ToLowerInvariant()}() in $filter. " +
                "String comparisons in Dataverse are already case-insensitive, so compare the column directly."),
            _ => throw new NotSupportedException(
                $"'{node}' cannot be filtered: Dataverse supports only Contains, StartsWith and EndsWith on strings in $filter."),
        };

        Visit(node.Object!);
        var target = _operandStack.Pop();

        _operandStack.Push($"{function}({target}, {FormatPattern(node.Arguments[0])})");
        return node;
    }

    // The text of contains/startswith/endswith. Dataverse matches these like SQL LIKE, so %, _ and [ in the text
    // would act as wildcards; they are escaped so the text matches literally, as string.Contains does.
    private string FormatPattern(Expression argument)
    {
        if (argument is ConstantExpression { Value: string text })
            return ODataLiteral.Quote(ODataLiteral.EscapeLikeWildcards(text));

        Visit(argument);
        return _operandStack.Pop();
    }

    private MethodCallExpression VisitStaticStringMethod(MethodCallExpression node)
    {
        switch (node.Method.Name)
        {
            case "IsNullOrEmpty":
                Visit(node.Arguments[0]);
                var fieldName = _operandStack.Pop();
                _operandStack.Push($"({fieldName} eq null or {fieldName} eq '')");
                break;

            case "Concat":
                throw new NotSupportedException(
                    $"'{node}' cannot be filtered: Dataverse does not support concat() in $filter.");

            default:
                throw new NotSupportedException($"Static string method '{node.Method.Name}' is not supported in OData filters.");
        }

        return node;
    }

    /// <summary>
    /// Translates a <see cref="DataverseFunctions"/> marker call into the corresponding
    /// <c>Microsoft.Dynamics.CRM.&lt;Name&gt;(PropertyName='field'[,PropertyValue=...|PropertyValues=[...]])</c>
    /// OData function syntax. The first argument is always the target column; remaining arguments
    /// supply the function's value(s).
    /// </summary>
    private MethodCallExpression VisitDataverseFunction(MethodCallExpression node)
    {
        // First argument is the column the function operates on. Query functions take the column's logical
        // name: a lookup is "ownerid" here, not its "_ownerid_value" read name, which Dataverse rejects.
        var column = node.Arguments[0];
        while (column is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
            column = convert.Operand;

        string propertyName;
        if (column is MemberExpression { Expression: ParameterExpression } member)
        {
            propertyName = EntityTypeMetadata.GetLogicalName(member.Member);
        }
        else
        {
            Visit(node.Arguments[0]);
            propertyName = _operandStack.Pop();
        }

        var builder = new StringBuilder($"Microsoft.Dynamics.CRM.{node.Method.Name}(PropertyName='{propertyName}'");

        if (node.Arguments.Count == 2 && node.Method.GetParameters()[1].ParameterType.IsArray)
        {
            // params object[] values → PropertyValues=["a","b",...]. An array variable is passed as is, and
            // a value-type array (not an object[]) arrives wrapped as the single element, so nested
            // collections are flattened.
            var values = FlattenValues(PartialEvaluator.EvaluateValue(node.Arguments[1]));
            builder.Append(CultureInfo.InvariantCulture, $",PropertyValues={FormatFunctionValues(values)}");
        }
        else if (node.Arguments.Count > 1)
        {
            // A single scalar value → PropertyValue='...'
            var value = PartialEvaluator.EvaluateValue(node.Arguments[1]);
            builder.Append(CultureInfo.InvariantCulture, $",PropertyValue={FormatFunctionValue(value)}");
        }

        builder.Append(')');
        _operandStack.Push(builder.ToString());
        return node;
    }

    /// <summary>
    /// Formats a Dataverse query-function parameter value. CRM function parameters are passed as
    /// quoted string literals (e.g., <c>'30'</c>, <c>'00000000-...'</c>).
    /// </summary>
    private static string FormatFunctionValue(object? value)
        => $"'{FunctionValueText(value).Replace("'", "''")}'";

    /// <summary>
    /// Formats the <c>PropertyValues</c> array of a Dataverse query function as a JSON array of strings
    /// (e.g., <c>["a","O'Brien"]</c>). Dataverse parses the array as JSON: single-quoted elements fail with
    /// "Invalid JSON" when a value contains an apostrophe.
    /// </summary>
    private static string FormatFunctionValues(IEnumerable<object?> values)
        => "[" + string.Join(",", values.Select(v => JsonSerializer.Serialize(FunctionValueText(v), FunctionValueJson))) + "]";

    // Keeps apostrophes and other characters literal; only quotes, backslashes and control characters are escaped.
    private static readonly JsonSerializerOptions FunctionValueJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string FunctionValueText(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        Guid g => g.ToString(),
        // Choice (option set) values are matched by their integer value, not the enum member name.
        Enum e => Convert.ToString(
            Convert.ChangeType(e, Enum.GetUnderlyingType(e.GetType()), CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture)!,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static IEnumerable<object?> FlattenValues(object? values)
    {
        if (values is not IEnumerable enumerable || values is string)
        {
            yield return values;
            yield break;
        }

        foreach (var value in enumerable)
        {
            if (value is IEnumerable nested and not string)
            {
                foreach (var inner in nested)
                    yield return inner;
            }
            else
            {
                yield return value;
            }
        }
    }

    private Expression VisitEnumerableContains(Expression source, Expression item)
    {
        // On .NET 10+ the source may be wrapped in ReadOnlySpan<T>.op_Implicit, which EvaluateValue unwraps.
        var collection = PartialEvaluator.EvaluateValue(source);
        if (collection is not IEnumerable enumerable)
        {
            throw new NotSupportedException("The source of Contains() must be an IEnumerable.");
        }

        var values = enumerable.Cast<object?>().ToList();
        if (values.Count == 0)
        {
            throw new ArgumentException("The collection used in Contains() must have at least one element.");
        }

        // Dataverse rejects the OData "in" operator (HTTP 501), so list.Contains(x.Column) is sent as the equivalent
        // Microsoft.Dynamics.CRM.In query function, which takes the column's logical name.
        while (item is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
            item = convert.Operand;

        if (item is not MemberExpression { Expression: ParameterExpression } column)
        {
            throw new NotSupportedException(
                $"'{item}' cannot be used with Contains(): the item must be a column of the record (e.g., ids.Contains(x.AccountId)).");
        }

        if (values.Contains(null))
            throw new NotSupportedException("The collection used in Contains() cannot contain null; test the column for null separately.");

        var propertyName = EntityTypeMetadata.GetLogicalName(column.Member);
        _operandStack.Push($"Microsoft.Dynamics.CRM.In(PropertyName='{propertyName}',PropertyValues={FormatFunctionValues(values)})");
        return source;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Not)
        {
            Visit(node.Operand);
            var operand = _operandStack.Pop();
            _operandStack.Push($"not ({operand})");
            return node;
        }

        if (node.NodeType == ExpressionType.Convert || node.NodeType == ExpressionType.ConvertChecked)
        {
            return Visit(node.Operand)!;
        }

        throw new NotSupportedException($"Unary operator '{node.NodeType}' is not supported in OData filters.");
    }

    protected override Expression VisitLambda<TDelegate>(Expression<TDelegate> node)
    {
        Visit(node.Body);
        if (_operandStack.Count > 0)
        {
            _filter.Append(_operandStack.Pop());
        }
        return node;
    }

    private static string GetODataPropertyName(MemberInfo member)
    {
        return ODataPropertyNameCache.GetODataPropertyName(member);
    }

    /// <summary>
    /// Attempts to translate a member-access chain rooted at the lambda parameter
    /// (e.g., <c>x.PrimaryContact.LastName</c>) into an OData navigation path
    /// (e.g., <c>primarycontact/lastname</c>). Returns <see langword="false"/> for chains
    /// that are not rooted at the parameter (closures, captured variables, etc.), which
    /// should be evaluated to their runtime value instead.
    /// </summary>
    private static bool TryGetNavigationPath(MemberExpression node, out string path)
    {
        path = string.Empty;

        // Walk inward collecting member names until we reach the parameter root.
        var segments = new Stack<string>();
        Expression? current = node;

        while (current is MemberExpression me)
        {
            // Only a related record can be navigated through: a member of a scalar column
            // (e.g. x.CreatedOn.Year) has no OData path.
            if (me != node && (me.Type.IsValueType || me.Type == typeof(string)))
                return false;

            segments.Push(GetODataPropertyName(me.Member));
            current = me.Expression;
        }

        if (current?.NodeType != ExpressionType.Parameter || segments.Count < 2)
        {
            // Either not rooted at the parameter, or a single-hop member (handled elsewhere).
            return false;
        }

        path = string.Join("/", segments);
        return true;
    }

    private static bool IsNullableType(Type? type)
    {
        return type is { IsGenericType: true } && type.GetGenericTypeDefinition() == typeof(Nullable<>);
    }

    private static bool IsCollectionType(Type type)
    {
        if (type == typeof(string))
            return false;

        return type.IsArray
            || type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
    }
}
