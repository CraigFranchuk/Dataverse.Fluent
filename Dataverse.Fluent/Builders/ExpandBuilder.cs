using System.Linq.Expressions;
using Dataverse.Fluent.Builders.Expressions;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Fluent builder for configuring an OData $expand clause with nested query options.
/// Supports $select, $filter, $orderby, and $top within the expansion.
/// </summary>
/// <typeparam name="TParent">The parent entity type that owns the navigation property.</typeparam>
/// <typeparam name="TRelated">The related entity type being expanded.</typeparam>
public sealed class ExpandBuilder<TParent, TRelated>
{
    private readonly string _navigationProperty;
    private readonly List<string> _selectProperties = [];
    private readonly List<string> _filters = [];
    private string? _orderBy;
    private int? _top;

    internal ExpandBuilder(string navigationProperty)
    {
        _navigationProperty = navigationProperty;
    }

    /// <summary>
    /// Adds $select to the expansion using raw property names.
    /// </summary>
    public ExpandBuilder<TParent, TRelated> Select(params string[] properties)
    {
        if (properties?.Length > 0)
        {
            _selectProperties.AddRange(properties);
        }
        return this;
    }

    /// <summary>
    /// Adds $select to the expansion using a lambda expression.
    /// </summary>
    public ExpandBuilder<TParent, TRelated> Select<TProjection>(Expression<Func<TRelated, TProjection>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        _selectProperties.AddRange(MemberPathExtractor.GetPropertyNames(selector));
        return this;
    }

    /// <summary>
    /// Adds $filter to the expansion using a raw OData filter string. Repeated calls are combined with <c>and</c>.
    /// </summary>
    public ExpandBuilder<TParent, TRelated> Where(string filter)
    {
        if (!string.IsNullOrWhiteSpace(filter))
        {
            _filters.Add(filter);
        }
        return this;
    }

    /// <summary>
    /// Adds $filter to the expansion using a lambda expression. Repeated calls are combined with <c>and</c>.
    /// </summary>
    public ExpandBuilder<TParent, TRelated> Where(Expression<Func<TRelated, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var visitor = new ODataExpressionVisitor();
        var odataFilter = visitor.ToODataFilter(predicate);

        if (!string.IsNullOrWhiteSpace(odataFilter))
        {
            _filters.Add(odataFilter);
        }
        return this;
    }

    /// <summary>
    /// Adds $orderby to the expansion using a raw OData orderby string.
    /// </summary>
    public ExpandBuilder<TParent, TRelated> OrderBy(string orderBy)
    {
        if (!string.IsNullOrWhiteSpace(orderBy))
        {
            _orderBy = orderBy;
        }
        return this;
    }

    /// <summary>
    /// Adds $orderby to the expansion using a lambda expression.
    /// </summary>
    public ExpandBuilder<TParent, TRelated> OrderBy<TKey>(Expression<Func<TRelated, TKey>> keySelector, bool descending = false)
    {
        ArgumentNullException.ThrowIfNull(keySelector);

        var propertyNames = MemberPathExtractor.GetPropertyNames(keySelector);
        if (propertyNames.Count > 0)
        {
            var direction = descending ? " desc" : " asc";
            _orderBy = string.Join(",", propertyNames.Select(p => p + direction));
        }
        return this;
    }

    /// <summary>
    /// Adds $top to the expansion.
    /// </summary>
    public ExpandBuilder<TParent, TRelated> Top(int top)
    {
        _top = top > 0 ? top : throw new ArgumentOutOfRangeException(nameof(top));
        return this;
    }

    /// <summary>
    /// Builds the expand clause string (e.g., "contact_customer_accounts($select=firstname,lastname;$top=5)").
    /// </summary>
    internal string Build()
    {
        var nestedOptions = new List<string>();

        if (_selectProperties.Count > 0)
        {
            nestedOptions.Add($"$select={string.Join(",", _selectProperties)}");
        }

        if (ODataQueryOptions.CombineFilters(_filters) is { } filter)
        {
            nestedOptions.Add($"$filter={filter}");
        }

        if (!string.IsNullOrWhiteSpace(_orderBy))
        {
            nestedOptions.Add($"$orderby={_orderBy}");
        }

        if (_top.HasValue)
        {
            nestedOptions.Add($"$top={_top.Value}");
        }

        if (nestedOptions.Count > 0)
        {
            return $"{_navigationProperty}({string.Join(";", nestedOptions)})";
        }

        return _navigationProperty;
    }
}
