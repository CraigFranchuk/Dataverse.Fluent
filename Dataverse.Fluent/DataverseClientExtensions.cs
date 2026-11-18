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
}
