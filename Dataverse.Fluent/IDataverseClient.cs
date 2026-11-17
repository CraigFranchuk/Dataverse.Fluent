using Dataverse.Fluent.Builders;

namespace Dataverse.Fluent;

/// <summary>
/// The fluent layer of the client: entry points for queries, CRUD, batches, actions, functions and FetchXML.
/// Requests are sent through <see cref="Transport"/>.
/// </summary>
/// <remarks>
/// Higher-level libraries (e.g., Dynamics 365 Sales) compose this interface. To unit test code that uses
/// the fluent API, fake <see cref="IDataverseTransport"/> and wrap it with
/// <see cref="DataverseClient(IDataverseTransport)"/>; the builders then run unchanged against the fake.
/// </remarks>
public interface IDataverseClient
{
    /// <summary>
    /// The transport the builders send requests through. Use it to call the Web API directly
    /// (<c>client.Transport.ExecuteRequestAsync(...)</c>) when no builder covers a request.
    /// </summary>
    IDataverseTransport Transport { get; }

    /// <summary>
    /// Creates a fluent query builder for RetrieveMultiple operations.
    /// </summary>
    QueryBuilder<T> Query<T>(string? entitySet = null);

    /// <summary>
    /// Creates a fluent builder for server-side aggregations using the OData <c>$apply</c> query option
    /// (groupby, sum, average, min, max, countdistinct, count).
    /// </summary>
    AggregateBuilder<T> Aggregate<T>(string? entitySet = null);

    /// <summary>
    /// Creates a fluent entity builder for single-entity CRUD operations.
    /// </summary>
    EntityBuilder<T> Entity<T>(Guid? id = null, string? entitySet = null);

    /// <summary>
    /// Creates a fluent batch builder for multi-operation requests.
    /// </summary>
    BatchBuilder Batch(Guid? batchId = null);

    /// <summary>
    /// Creates a fluent annotation (note/attachment) builder.
    /// </summary>
    AnnotationBuilder Annotation(Guid? annotationId = null);

    /// <summary>
    /// Creates a fluent builder for delta (change tracking) queries.
    /// </summary>
    DeltaBuilder<T> Delta<T>(string? entitySet = null);

    /// <summary>
    /// Creates a fluent builder for executing an unbound action.
    /// </summary>
    ActionBuilder<TRequest, TResponse> Action<TRequest, TResponse>(string actionName)
        where TRequest : class;

    /// <summary>
    /// Creates a fluent builder for executing an unbound function.
    /// </summary>
    FunctionBuilder<TResponse> Function<TResponse>(string functionName);

    /// <summary>
    /// Creates a fluent builder for executing bound actions and functions on a specific entity.
    /// </summary>
    EntityFluentBuilder<T> ForEntity<T>(Guid entityId, string? entitySet = null);

    /// <summary>
    /// Creates a fluent FetchXML builder for constructing strongly-typed FetchXML queries.
    /// </summary>
    FetchBuilder<T> FetchBuilder<T>(string? entityLogicalName = null, FetchXmlOptions? options = null);

    /// <summary>
    /// Executes a raw FetchXML query and returns strongly-typed results.
    /// </summary>
    FetchQueryExecutor<T> Fetch<T>(string fetchXml, FetchXmlOptions? options = null);

    /// <summary>
    /// Creates a fluent builder for deep insert operations.
    /// </summary>
    DeepInsertBuilder<T> DeepInsert<T>(T parentEntity, string? entitySet = null);

    /// <summary>
    /// Creates a fluent builder for associating or disassociating entities via navigation properties.
    /// </summary>
    AssociationBuilder Associate();
}
