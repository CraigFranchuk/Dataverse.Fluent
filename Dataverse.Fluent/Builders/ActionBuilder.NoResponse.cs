using System.Linq.Expressions;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Fluent builder for an action that returns nothing, with its parameters on a request class. Created by
/// <c>client.Action&lt;TRequest&gt;(name)</c> or <c>client.ForEntity&lt;T&gt;(id).Action&lt;TRequest&gt;(name)</c>.
/// </summary>
/// <typeparam name="TRequest">The request POCO type containing action parameters.</typeparam>
/// <remarks>
/// This type is not thread-safe. Each instance is for a single operation: <see cref="ExecuteAsync"/> and
/// <see cref="ToRequest"/> mark the builder as consumed, and a second call throws <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class ActionBuilder<TRequest> where TRequest : class
{
    private readonly ActionBuilder<TRequest, object> _inner;

    internal ActionBuilder(IDataverseTransport client, string actionName, string? entitySet = null, Guid? entityId = null)
        => _inner = new ActionBuilder<TRequest, object>(client, actionName, entitySet, entityId);

    /// <summary>
    /// Sets a parameter value using a strongly-typed expression.
    /// </summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="propertyExpression">Expression pointing to the property (e.g., p => p.Target).</param>
    /// <param name="value">The value to set.</param>
    public ActionBuilder<TRequest> WithParameter<TProperty>(Expression<Func<TRequest, TProperty>> propertyExpression, TProperty value)
    {
        _inner.WithParameter(propertyExpression, value);
        return this;
    }

    /// <summary>
    /// Sets a parameter value by name.
    /// </summary>
    /// <param name="parameterName">The parameter name, as the action declares it.</param>
    /// <param name="value">The value to set.</param>
    public ActionBuilder<TRequest> WithParameter(string parameterName, object? value)
    {
        _inner.WithParameter(parameterName, value);
        return this;
    }

    /// <summary>
    /// Sets all parameters from a request instance.
    /// </summary>
    /// <param name="request">The request instance to copy parameters from.</param>
    public ActionBuilder<TRequest> WithRequest(TRequest request)
    {
        _inner.WithRequest(request);
        return this;
    }

    /// <summary>
    /// Executes the action. Any response body is ignored.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    public Task ExecuteAsync(CancellationToken ct = default) => _inner.ExecuteAsync(ct);

    /// <summary>
    /// Builds the request <see cref="ExecuteAsync"/> would send, without sending it, so the action can be
    /// queued in a batch with <see cref="BatchBuilder.Add(DataverseRequest)"/>. The builder is consumed.
    /// </summary>
    public DataverseRequest ToRequest() => _inner.ToRequest();
}

/// <summary>
/// Fluent builder for an action whose parameters are given by name, without a request class. Created by
/// <c>client.Action(name)</c> or <c>client.ForEntity&lt;T&gt;(id).Action(name)</c>.
/// </summary>
/// <remarks>
/// This type is not thread-safe. Each instance is for a single operation: the <c>ExecuteAsync</c> methods and
/// <see cref="ToRequest"/> mark the builder as consumed, and a second call throws <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class ActionBuilder
{
    private readonly IDataverseTransport _client;
    private readonly ActionBuilder<object, object> _inner;

    internal ActionBuilder(IDataverseTransport client, string actionName, string? entitySet = null, Guid? entityId = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _inner = new ActionBuilder<object, object>(client, actionName, entitySet, entityId);
    }

    /// <summary>
    /// Sets a parameter value by name.
    /// </summary>
    /// <param name="parameterName">The parameter name, as the action declares it.</param>
    /// <param name="value">The value to set. An enum is written by member name, as Dataverse requires.</param>
    public ActionBuilder WithParameter(string parameterName, object? value)
    {
        _inner.WithParameter(parameterName, value);
        return this;
    }

    /// <summary>
    /// Executes the action. Any response body is ignored.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    public Task ExecuteAsync(CancellationToken ct = default) => _inner.ExecuteAsync(ct);

    /// <summary>
    /// Executes the action and returns its response.
    /// </summary>
    /// <typeparam name="TResponse">The response POCO type for the action result.</typeparam>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task<TResponse?> ExecuteAsync<TResponse>(CancellationToken ct = default)
    {
        using var response = await _client.SendAsync(_inner.ToRequest(), ct).ConfigureAwait(false);
        return await response.ReadEntityAsync<TResponse>(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the request the <c>ExecuteAsync</c> methods would send, without sending it, so the action can be
    /// queued in a batch with <see cref="BatchBuilder.Add(DataverseRequest)"/>. The builder is consumed.
    /// </summary>
    public DataverseRequest ToRequest() => _inner.ToRequest();
}
