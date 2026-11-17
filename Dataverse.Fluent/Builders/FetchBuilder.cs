using System.Linq.Expressions;
using Dataverse.Fluent.Builders.Expressions;
using System.Text;
using System.Xml;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Fluent builder for constructing and executing FetchXML queries with strong typing.
/// </summary>
/// <typeparam name="T">The POCO type to deserialize results into.</typeparam>
/// <remarks>
/// This type is not thread-safe. Each instance should be used for a single operation and not shared across threads.
/// Terminal methods (e.g., <see cref="ExecuteAsync"/>, <see cref="StreamAsync"/>) mark the builder as consumed;
/// subsequent calls will throw <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class FetchBuilder<T>
{
    private readonly IDataverseTransport _client;
    private readonly string _entityLogicalName;
    private readonly List<string> _attributes = [];
    private readonly List<(string attribute, bool descending)> _orders = [];
    private readonly List<string> _filters = [];

    private bool _allAttributes;
    private int? _top;
    private int? _pageNumber;
    private int? _pageSize;
    private bool _noLock;
    private bool _distinct;
    private string _filterOperator = "and";
    private FetchXmlOptions _options = FetchXmlOptions.Default;
    private SingleUseGuard _guard;

    internal FetchBuilder(IDataverseTransport client, string? entityLogicalName = null, FetchXmlOptions? options = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _entityLogicalName = entityLogicalName ?? InferEntityLogicalName();
        _options = options ?? FetchXmlOptions.Default;
    }

    /// <summary>
    /// Adds an attribute (column) to select using a property expression.
    /// </summary>
    /// <param name="selector">Expression selecting the property to include.</param>
    public FetchBuilder<T> AddAttribute<TProperty>(Expression<Func<T, TProperty>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var propertyName = EntityTypeMetadata.GetLogicalName(MemberPathExtractor.GetProperty(selector));
        if (!_attributes.Contains(propertyName))
        {
            _attributes.Add(propertyName);
        }
        
        return this;
    }

    /// <summary>
    /// Adds multiple attributes to select using a projection expression.
    /// </summary>
    /// <param name="selector">Expression selecting multiple properties (e.g., e => new { e.Name, e.CreatedOn }).</param>
    public FetchBuilder<T> AddAttributes<TProjection>(Expression<Func<T, TProjection>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var propertyNames = MemberPathExtractor.GetMembers(selector).ConvertAll(EntityTypeMetadata.GetLogicalName);
        foreach (var propertyName in propertyNames)
        {
            if (!_attributes.Contains(propertyName))
            {
                _attributes.Add(propertyName);
            }
        }
        
        return this;
    }

    /// <summary>
    /// Adds all attributes (*) to the select list.
    /// </summary>
    public FetchBuilder<T> AddAllAttributes()
    {
        _allAttributes = true;
        _attributes.Clear();
        return this;
    }

    /// <summary>
    /// Adds a filter condition using a predicate expression.
    /// </summary>
    /// <param name="predicate">Expression defining the filter condition.</param>
    /// <param name="filterOperator">Filter operator (and/or). Default is "and".</param>
    public FetchBuilder<T> AddFilter(Expression<Func<T, bool>> predicate, string filterOperator = "and")
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var visitor = new FetchXmlFilterVisitor();
        var filterXml = visitor.ToFetchXmlFilter(predicate);
        
        if (!string.IsNullOrWhiteSpace(filterXml))
        {
            _filters.Add(filterXml);
            _filterOperator = filterOperator;
        }
        
        return this;
    }

    /// <summary>
    /// Adds an order clause using a property expression.
    /// </summary>
    /// <param name="selector">Expression selecting the property to order by.</param>
    /// <param name="descending">Whether to sort in descending order.</param>
    public FetchBuilder<T> AddOrder<TProperty>(Expression<Func<T, TProperty>> selector, bool descending = false)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var propertyName = EntityTypeMetadata.GetLogicalName(MemberPathExtractor.GetProperty(selector));
        _orders.Add((propertyName, descending));
        
        return this;
    }

    /// <summary>
    /// Sets the maximum number of records to retrieve (top N).
    /// </summary>
    public FetchBuilder<T> Top(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        _top = count;
        return this;
    }

    /// <summary>
    /// Sets the page number and page size for pagination.
    /// </summary>
    /// <param name="page">The page number (1-based).</param>
    /// <param name="pageSize">The number of records per page.</param>
    public FetchBuilder<T> Page(int page, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        
        _pageNumber = page;
        _pageSize = pageSize;
        return this;
    }

    /// <summary>
    /// Sets whether to use no-lock hint for the query.
    /// </summary>
    public FetchBuilder<T> NoLock(bool enabled = true)
    {
        _noLock = enabled;
        return this;
    }

    /// <summary>
    /// Sets whether to include distinct results.
    /// </summary>
    public FetchBuilder<T> Distinct(bool enabled = true)
    {
        _distinct = enabled;
        return this;
    }

    /// <summary>
    /// Builds the FetchXML string without executing it.
    /// </summary>
    public string BuildFetchXml()
    {
        var sb = new StringBuilder();
        using var writer = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false });

        writer.WriteStartElement("fetch");
        
        if (_top.HasValue)
        {
            writer.WriteAttributeString("top", _top.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (_distinct)
        {
            writer.WriteAttributeString("distinct", "true");
        }

        if (_noLock)
        {
            writer.WriteAttributeString("no-lock", "true");
        }

        if (_pageNumber.HasValue && _pageSize.HasValue)
        {
            writer.WriteAttributeString("page", _pageNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteAttributeString("count", _pageSize.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        writer.WriteStartElement("entity");
        writer.WriteAttributeString("name", _entityLogicalName);

        // Attributes
        if (_allAttributes)
        {
            writer.WriteStartElement("all-attributes");
            writer.WriteEndElement();
        }
        else if (_attributes.Count > 0)
        {
            foreach (var attribute in _attributes)
            {
                writer.WriteStartElement("attribute");
                writer.WriteAttributeString("name", attribute);
                writer.WriteEndElement();
            }
        }

        // Filters
        if (_filters.Count > 0)
        {
            writer.WriteStartElement("filter");
            writer.WriteAttributeString("type", _filterOperator);
            
            foreach (var filter in _filters)
            {
                writer.WriteRaw(filter);
            }
            
            writer.WriteEndElement();
        }

        // Orders
        foreach (var (attribute, descending) in _orders)
        {
            writer.WriteStartElement("order");
            writer.WriteAttributeString("attribute", attribute);
            if (descending)
            {
                writer.WriteAttributeString("descending", "true");
            }
            writer.WriteEndElement();
        }

        writer.WriteEndElement(); // entity
        writer.WriteEndElement(); // fetch

        writer.Flush();
        return sb.ToString();
    }

    /// <summary>
    /// Builds and executes the FetchXML query, returning all results.
    /// </summary>
    public async Task<List<T>> ExecuteAsync(CancellationToken ct = default)
    {
        _guard.Use();
        var fetchXml = BuildFetchXml();
        var executor = new FetchXmlExecutor<T>(_client);
        return await executor.ExecuteAsync(fetchXml, _options, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Streams results with automatic pagination for large datasets.
    /// </summary>
    public IAsyncEnumerable<T> StreamAsync(CancellationToken ct = default)
    {
        _guard.Use();
        var fetchXml = BuildFetchXml();
        var executor = new FetchXmlExecutor<T>(_client);
        return executor.StreamAsync(fetchXml, _options, ct);
    }

    // A table's logical name is all lowercase; it cannot be derived from the entity set name.
    private static string InferEntityLogicalName()
        => DataverseTypeMapping.ResolveEntityLogicalName(typeof(T)) ?? typeof(T).Name.ToLowerInvariant();
}
