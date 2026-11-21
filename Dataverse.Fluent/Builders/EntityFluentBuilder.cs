namespace Dataverse.Fluent.Builders;

/// <summary>
/// Fluent builder for bound actions and functions on a specific entity type.
/// Provides access to Action() and Function() methods for entity-specific operations.
/// </summary>
/// <typeparam name="T">The POCO type for the entity.</typeparam>
public sealed class EntityFluentBuilder<T>
{
    private readonly IDataverseTransport _client;
    private readonly string _entitySet;
    private readonly Guid _entityId;

    internal EntityFluentBuilder(IDataverseTransport client, string entitySet, Guid entityId)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _entitySet = entitySet ?? throw new ArgumentNullException(nameof(entitySet));
        _entityId = entityId;
    }

    /// <summary>
    /// Creates a fluent builder for executing a bound action on this entity.
    /// </summary>
    /// <typeparam name="TRequest">The request POCO type containing action parameters.</typeparam>
    /// <typeparam name="TResponse">The response POCO type for the action result.</typeparam>
    /// <param name="actionName">The logical name of the bound action.</param>
    /// <returns>An action builder for chaining parameters and execution.</returns>
    public ActionBuilder<TRequest, TResponse> Action<TRequest, TResponse>(string actionName) 
        where TRequest : class
        => new ActionBuilder<TRequest, TResponse>(_client, actionName, _entitySet, _entityId);

    /// <summary>
    /// Creates a fluent builder for executing a bound action that returns nothing.
    /// </summary>
    /// <typeparam name="TRequest">The request POCO type containing action parameters.</typeparam>
    /// <param name="actionName">The logical name of the bound action.</param>
    public ActionBuilder<TRequest> Action<TRequest>(string actionName)
        where TRequest : class
        => new ActionBuilder<TRequest>(_client, actionName, _entitySet, _entityId);

    /// <summary>
    /// Creates a fluent builder for executing a bound action whose parameters are given by name, without a request class.
    /// </summary>
    /// <param name="actionName">The logical name of the bound action.</param>
    public ActionBuilder Action(string actionName)
        => new ActionBuilder(_client, actionName, _entitySet, _entityId);

    /// <summary>
    /// Creates a fluent builder for executing a bound function on this entity.
    /// </summary>
    /// <typeparam name="TResponse">The response POCO type for the function result.</typeparam>
    /// <param name="functionName">The logical name of the bound function.</param>
    /// <returns>A function builder for chaining parameters and execution.</returns>
    public FunctionBuilder<TResponse> Function<TResponse>(string functionName)
        => new FunctionBuilder<TResponse>(_client, functionName, _entitySet, _entityId);
}
