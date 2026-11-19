using System.Linq.Expressions;
using System.Text;
using Dataverse.Fluent.Builders.Expressions;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Fluent builder for Dataverse server-side aggregations using the OData <c>$apply</c> query option.
/// Supports <c>groupby</c> and the standard aggregate methods (<c>sum</c>, <c>average</c>, <c>min</c>,
/// <c>max</c>, <c>countdistinct</c>) as well as record counting (<c>$count</c>), with an optional
/// leading <c>filter(...)</c> transform.
/// </summary>
/// <typeparam name="T">The POCO type describing the source entity. Property names are resolved using the
/// same conventions as <see cref="QueryBuilder{T}"/> (registered resolvers, <see cref="DataverseColumnAttribute"/>,
/// <c>[JsonPropertyName]</c>, then lowercase member name).</typeparam>
/// <remarks>
/// This type is not thread-safe. Each instance should be used for a single operation and not shared across threads.
/// The terminal method <see cref="GetListAsync{TResult}"/> marks the builder as consumed; subsequent calls will throw
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class AggregateBuilder<T>
{
    private readonly IDataverseTransport _client;
    private readonly string _entitySet;

    private readonly List<string> _groupByProperties = [];
    private readonly List<string> _aggregateExpressions = [];
    private readonly List<string> _filters = [];
    private SingleUseGuard _guard;

    internal AggregateBuilder(IDataverseTransport client, string? entitySet = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _entitySet = entitySet ?? ODataPropertyNameCache.GetEntitySetName<T>();
    }

    /// <summary>
    /// Adds a leading <c>filter(...)</c> transform using a raw OData filter string.
    /// The filter is applied before grouping and aggregation. Repeated calls are combined with <c>and</c>.
    /// </summary>
    public AggregateBuilder<T> Where(string filter)
    {
        if (!string.IsNullOrWhiteSpace(filter))
        {
            _filters.Add(filter);
        }
        return this;
    }

    /// <summary>
    /// Adds a leading <c>filter(...)</c> transform using a lambda expression converted to OData syntax.
    /// The filter is applied before grouping and aggregation. Repeated calls are combined with <c>and</c>.
    /// </summary>
    /// <param name="predicate">Lambda expression defining the filter condition (e.g., x =&gt; x.Revenue &gt; 1000000).</param>
    public AggregateBuilder<T> Where(Expression<Func<T, bool>> predicate)
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
    /// Adds one or more grouping properties to a <c>groupby((...))</c> transform.
    /// Can be a single property (e.g., x =&gt; x.Industry) or a projection of several
    /// (e.g., x =&gt; new { x.Industry, x.StateCode }).
    /// </summary>
    /// <typeparam name="TKey">The property or projection type.</typeparam>
    /// <param name="keySelector">Lambda expression selecting the grouping property or properties.</param>
    /// <exception cref="NotSupportedException">A selected property is a lookup: OData <c>$apply</c> cannot group by a
    /// lookup's value. Use a FetchXML aggregate instead.</exception>
    public AggregateBuilder<T> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);

        foreach (var name in MemberPathExtractor.GetPropertyNames(keySelector))
        {
            ThrowIfLookup(name);
            if (!_groupByProperties.Contains(name))
            {
                _groupByProperties.Add(name);
            }
        }

        return this;
    }

    /// <summary>
    /// Adds a <c>sum</c> aggregate over the selected numeric property.
    /// </summary>
    /// <param name="selector">Lambda expression selecting the property to sum.</param>
    /// <param name="alias">The alias name the aggregated value is projected to in the result.</param>
    public AggregateBuilder<T> Sum<TProperty>(Expression<Func<T, TProperty>> selector, string alias)
        => AddAggregate(selector, "sum", alias);

    /// <summary>
    /// Adds an <c>average</c> aggregate over the selected numeric property.
    /// </summary>
    /// <param name="selector">Lambda expression selecting the property to average.</param>
    /// <param name="alias">The alias name the aggregated value is projected to in the result.</param>
    public AggregateBuilder<T> Average<TProperty>(Expression<Func<T, TProperty>> selector, string alias)
        => AddAggregate(selector, "average", alias);

    /// <summary>
    /// Adds a <c>min</c> aggregate over the selected property.
    /// </summary>
    /// <param name="selector">Lambda expression selecting the property to take the minimum of.</param>
    /// <param name="alias">The alias name the aggregated value is projected to in the result.</param>
    public AggregateBuilder<T> Min<TProperty>(Expression<Func<T, TProperty>> selector, string alias)
        => AddAggregate(selector, "min", alias);

    /// <summary>
    /// Adds a <c>max</c> aggregate over the selected property.
    /// </summary>
    /// <param name="selector">Lambda expression selecting the property to take the maximum of.</param>
    /// <param name="alias">The alias name the aggregated value is projected to in the result.</param>
    public AggregateBuilder<T> Max<TProperty>(Expression<Func<T, TProperty>> selector, string alias)
        => AddAggregate(selector, "max", alias);

    /// <summary>
    /// Adds a <c>countdistinct</c> aggregate over the selected property.
    /// </summary>
    /// <param name="selector">Lambda expression selecting the property to count distinct values of.</param>
    /// <param name="alias">The alias name the aggregated value is projected to in the result.</param>
    public AggregateBuilder<T> CountDistinct<TProperty>(Expression<Func<T, TProperty>> selector, string alias)
        => AddAggregate(selector, "countdistinct", alias);

    /// <summary>
    /// Adds a record count aggregate (<c>$count as alias</c>).
    /// </summary>
    /// <param name="alias">The alias name the record count is projected to in the result.</param>
    public AggregateBuilder<T> Count(string alias)
    {
        ValidateAlias(alias);
        _aggregateExpressions.Add($"$count as {alias}");
        return this;
    }

    private AggregateBuilder<T> AddAggregate<TProperty>(Expression<Func<T, TProperty>> selector, string aggregateMethod, string alias)
    {
        ArgumentNullException.ThrowIfNull(selector);

        ValidateAlias(alias);

        var propertyName = ExtractSinglePropertyName(selector);
        ThrowIfLookup(propertyName);
        _aggregateExpressions.Add($"{propertyName} with {aggregateMethod} as {alias}");
        return this;
    }

    /// <summary>
    /// Executes the aggregation and deserializes each result row into <typeparamref name="TResult"/>.
    /// Use <c>[JsonPropertyName("alias")]</c> on the result type's properties to map the aliases
    /// (and grouped property logical names) produced by the query.
    /// </summary>
    /// <typeparam name="TResult">The POCO type to deserialize each aggregation row into.</typeparam>
    /// <param name="ct">Cancellation token.</param>
    public async Task<List<TResult>> GetListAsync<TResult>(CancellationToken ct = default)
    {
        _guard.Use();

        var url = _entitySet + "?$apply=" + Uri.EscapeDataString(BuildApply());
        var response = await RequestSender.ExecuteAsync<Models.ODataCollectionResponse<TResult>>(_client, HttpMethod.Get, url, null, ct).ConfigureAwait(false);

        return response?.GetItems() ?? [];
    }

    // Assembles the $apply transform string in canonical order: filter(...)/groupby((...),aggregate(...)).
    internal string BuildApply()
    {
        if (_aggregateExpressions.Count == 0 && _groupByProperties.Count == 0)
        {
            throw new InvalidOperationException(
                "An aggregation must specify at least one aggregate (e.g., Sum/Average/Min/Max/CountDistinct/Count) or a GroupBy.");
        }

        var sb = new StringBuilder();

        if (ODataQueryOptions.CombineFilters(_filters) is { } filter)
        {
            sb.Append("filter(").Append(filter).Append(")/");
        }

        var aggregate = _aggregateExpressions.Count > 0
            ? "aggregate(" + string.Join(",", _aggregateExpressions) + ")"
            : null;

        if (_groupByProperties.Count > 0)
        {
            sb.Append("groupby((").Append(string.Join(",", _groupByProperties)).Append(')');
            if (aggregate != null)
            {
                sb.Append(',').Append(aggregate);
            }
            sb.Append(')');
        }
        else
        {
            sb.Append(aggregate);
        }

        return sb.ToString();
    }

    private static void ValidateAlias(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new ArgumentException("Aggregate alias cannot be null or whitespace.", nameof(alias));
        }
    }

    // Dataverse rejects a lookup in $apply under every name: "_ownerid_value" is not an attribute there, and
    // "ownerid" is a navigation property, not a value. A lookup is read and selected as _{column}_value.
    private static void ThrowIfLookup(string propertyName)
    {
        if (propertyName.Length > "__value".Length && propertyName.StartsWith('_') && propertyName.EndsWith("_value", StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"'{propertyName}' is a lookup, which OData $apply cannot group or aggregate by. Group by a column of the related " +
                "record with a raw $apply (e.g., groupby((primarycontactid/fullname))), or use a FetchXML aggregate, which " +
                "supports lookups.");
        }
    }

    private static string ExtractSinglePropertyName<TProperty>(Expression<Func<T, TProperty>> selector)
    {
        var names = MemberPathExtractor.GetPropertyNames(selector);
        if (names.Count != 1)
        {
            throw new ArgumentException("Aggregate selector must reference exactly one property.", nameof(selector));
        }
        return names[0];
    }
}
