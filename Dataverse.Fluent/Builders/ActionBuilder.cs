using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dataverse.Fluent.Builders.Expressions;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Implementation of fluent builder for executing Dataverse actions.
/// Supports both bound and unbound actions with strongly-typed parameters.
/// </summary>
/// <typeparam name="TRequest">The request POCO type containing action parameters.</typeparam>
/// <typeparam name="TResponse">The response POCO type for the action result.</typeparam>
/// <remarks>
/// This type is not thread-safe. Each instance should be used for a single operation and not shared across threads.
/// Terminal methods (e.g., <see cref="ExecuteAsync"/>) mark the builder as consumed; subsequent calls will throw
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class ActionBuilder<TRequest, TResponse> where TRequest : class
{
    private readonly IDataverseTransport _client;
    private readonly string _actionName;
    private readonly string? _entitySet;
    private readonly Guid? _entityId;
    private readonly JsonObject _parameters = new();
    private SingleUseGuard _guard;

    internal ActionBuilder(IDataverseTransport client, string actionName, string? entitySet = null, Guid? entityId = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _actionName = actionName ?? throw new ArgumentNullException(nameof(actionName));
        _entitySet = entitySet;
        _entityId = entityId;
    }

    /// <summary>
    /// Sets a parameter value using a strongly-typed expression.
    /// </summary>
    /// <typeparam name="TProperty">The property type</typeparam>
    /// <param name="propertyExpression">Expression pointing to the property (e.g., p => p.Target)</param>
    /// <param name="value">The value to set</param>
    public ActionBuilder<TRequest, TResponse> WithParameter<TProperty>(
        Expression<Func<TRequest, TProperty>> propertyExpression,
        TProperty value)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);

        var parameterName = GetJsonPropertyName(propertyExpression);
        return WithParameter(parameterName, value);
    }

    /// <summary>
    /// Sets a parameter value by name (fallback for non-strongly-typed scenarios).
    /// </summary>
    /// <param name="parameterName">The parameter name (logical name)</param>
    /// <param name="value">The value to set</param>
    public ActionBuilder<TRequest, TResponse> WithParameter(string parameterName, object? value)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
            throw new ArgumentNullException(nameof(parameterName));

        if (value != null)
        {
            var serialized = JsonSerializer.SerializeToNode(value, value.GetType(), JsonDefaults.PayloadSerialization);
            _parameters[parameterName] = serialized;
        }
        else
        {
            _parameters[parameterName] = null;
        }

        return this;
    }

    /// <summary>
    /// Sets all parameters from a request instance.
    /// </summary>
    /// <param name="request">The request instance to copy parameters from</param>
    public ActionBuilder<TRequest, TResponse> WithRequest(TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var serialized = JsonSerializer.SerializeToNode(request, JsonDefaults.IgnoreNull);
        if (serialized is JsonObject obj)
        {
            foreach (var prop in obj)
            {
                _parameters[prop.Key] = prop.Value?.DeepClone();
            }
        }

        return this;
    }

    /// <summary>
    /// Executes the action and returns the response.
    /// </summary>
    /// <param name="ct">Optional cancellation token</param>
    public async Task<TResponse?> ExecuteAsync(CancellationToken ct = default)
    {
        _guard.Use();
        using var response = await _client.SendAsync(BuildRequest(), ct).ConfigureAwait(false);

        if (typeof(TResponse) == typeof(object))
        {
            return default;
        }

        return await response.ReadEntityAsync<TResponse>(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the request <see cref="ExecuteAsync"/> would send, without sending it, so the action can be
    /// queued in a batch with <see cref="BatchBuilder.Add(DataverseRequest)"/>. The builder is consumed.
    /// </summary>
    public DataverseRequest ToRequest()
    {
        _guard.Use();
        return BuildRequest();
    }

    private DataverseRequest BuildRequest()
        => new(HttpMethod.Post, BuildActionUrl()) { Payload = _parameters.DeepClone().AsObject() };

    private string BuildActionUrl()
    {
        if (_entitySet != null && _entityId.HasValue)
        {
            return $"{_entitySet}({_entityId.Value})/Microsoft.Dynamics.CRM.{_actionName}";
        }

        return _actionName;
    }

    // The parameter name is the one the serializer writes for the property, so WithParameter(x => x.P, v)
    // and WithRequest(new TRequest { P = v }) always produce the same payload.
    private static string GetJsonPropertyName<TProperty>(Expression<Func<TRequest, TProperty>> propertyExpression)
    {
        var property = MemberPathExtractor.GetProperty(propertyExpression);

        foreach (var candidate in JsonDefaults.PayloadSerialization.GetTypeInfo(typeof(TRequest)).Properties)
        {
            if (candidate.AttributeProvider is MemberInfo member
                && member.Name == property.Name
                && member.DeclaringType == property.DeclaringType)
            {
                return candidate.Name;
            }
        }

        return ODataPropertyNameCache.GetODataPropertyName(property);
    }
}
