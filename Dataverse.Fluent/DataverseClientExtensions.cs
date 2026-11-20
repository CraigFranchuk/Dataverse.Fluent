using Dataverse.Fluent.Builders;

namespace Dataverse.Fluent;

/// <summary>
/// Extension methods for <see cref="IDataverseClient"/>.
/// </summary>
public static class DataverseClientExtensions
{
    /// <summary>
    /// Returns a client that sends the headers of <see cref="DataverseRequestOptions"/> with every request, for
    /// example to bypass custom business logic during a data load or to impersonate another user.
    /// </summary>
    /// <param name="client">The client to derive from. It is not changed.</param>
    /// <param name="configure">Sets the options.</param>
    /// <returns>
    /// A client over the same transport. It is cheap to create, needs no disposal, and must not be used after
    /// <paramref name="client"/> is disposed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The headers apply to every request the returned client sends, including each operation of a batch: Dataverse
    /// does not apply the headers of a <c>$batch</c> request to the operations inside it. A header that a request sets
    /// itself takes precedence.
    /// </para>
    /// <para>
    /// Calling <c>WithOptions</c> on a client returned by <c>WithOptions</c> adds to its options; an option set by both
    /// calls takes the newer value. When the options set nothing, <paramref name="client"/> itself is returned.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The options are invalid, for example both impersonation IDs are set.</exception>
    /// <example>
    /// <code>
    /// var loader = client.WithOptions(o => o.BypassBusinessLogic = BusinessLogicBypass.CustomSync);
    /// await loader.Batch().AddCreate(account).AddCreate(contact).ExecuteAsync();
    /// </code>
    /// </example>
    public static IDataverseClient WithOptions(this IDataverseClient client, Action<DataverseRequestOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new DataverseRequestOptions();
        configure(options);
        return client.WithOptions(options);
    }

    /// <summary>
    /// Returns a client that sends the headers of <paramref name="options"/> with every request. See
    /// <see cref="WithOptions(IDataverseClient, Action{DataverseRequestOptions})"/>.
    /// </summary>
    /// <param name="client">The client to derive from. It is not changed.</param>
    /// <param name="options">The options. They are read once: changing them afterwards does not affect the returned client.</param>
    /// <returns>A client over the same transport that sends the options' headers.</returns>
    /// <exception cref="ArgumentException">The options are invalid, for example both impersonation IDs are set.</exception>
    public static IDataverseClient WithOptions(this IDataverseClient client, DataverseRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        var headers = options.BuildHeaders();
        if (headers.Count == 0)
            return client;

        return new DataverseClient(RequestOptionsTransport.Wrap(client.Transport, headers));
    }

    /// <summary>
    /// Creates a fluent builder for executing an unbound action that returns nothing.
    /// </summary>
    /// <typeparam name="TRequest">The request POCO type containing action parameters.</typeparam>
    /// <param name="client">The client to send through.</param>
    /// <param name="actionName">The logical name of the action.</param>
    /// <example>
    /// <code>
    /// await client.Action&lt;WinOpportunityRequest&gt;("WinOpportunity").WithRequest(request).ExecuteAsync();
    /// </code>
    /// </example>
    public static ActionBuilder<TRequest> Action<TRequest>(this IDataverseClient client, string actionName)
        where TRequest : class
    {
        ArgumentNullException.ThrowIfNull(client);
        return new ActionBuilder<TRequest>(client.Transport, actionName);
    }

    /// <summary>
    /// Creates a fluent builder for executing an unbound action whose parameters are given by name, without a
    /// request class.
    /// </summary>
    /// <param name="client">The client to send through.</param>
    /// <param name="actionName">The logical name of the action.</param>
    /// <example>
    /// <code>
    /// await client.Action("new_Recalculate").WithParameter("Mode", 2).ExecuteAsync();
    /// var score = await client.Action("new_Score").WithParameter("Mode", 2).ExecuteAsync&lt;ScoreResult&gt;();
    /// </code>
    /// </example>
    public static ActionBuilder Action(this IDataverseClient client, string actionName)
    {
        ArgumentNullException.ThrowIfNull(client);
        return new ActionBuilder(client.Transport, actionName);
    }

    /// <summary>
    /// Creates a builder that writes many records of one table with the bulk operation messages
    /// (<c>CreateMultiple</c>, <c>UpdateMultiple</c>, <c>UpsertMultiple</c>), in chunks and optionally in parallel.
    /// </summary>
    /// <typeparam name="T">The POCO type of the table.</typeparam>
    /// <param name="client">The client to send through. Options set with <c>WithOptions</c>, such as bypassing custom
    /// business logic, apply to every chunk.</param>
    /// <param name="entitySet">The entity set name; inferred from <typeparamref name="T"/> when omitted.</param>
    /// <param name="entityLogicalName">The table's logical name, sent as each record's <c>@odata.type</c>; resolved from
    /// <see cref="DataverseEntityAttribute"/> or a name resolver, or the lowercase type name, when omitted.</param>
    /// <example>
    /// <code>
    /// var result = await client.Bulk&lt;Account&gt;().ChunkSize(500).CreateAsync(accounts);
    /// result.ThrowIfAnyFailed();
    /// </code>
    /// </example>
    public static BulkBuilder<T> Bulk<T>(this IDataverseClient client, string? entitySet = null, string? entityLogicalName = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(client);
        return new BulkBuilder<T>(client.Transport, entitySet, entityLogicalName);
    }
}
