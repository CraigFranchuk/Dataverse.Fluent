namespace Dataverse.Fluent;

/// <summary>
/// The transport layer of the client: sends a <see cref="DataverseRequest"/> to the Dataverse Web API with
/// authentication, and turns failures into <see cref="Models.DataverseException"/>. The fluent builders
/// depend only on this interface.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DataverseHttpTransport"/> is the HTTP implementation. To run the fluent API over a different
/// transport, such as a test fake, a recording transport, or a decorator that adds caching or auditing, wrap it
/// with <see cref="DataverseClient(IDataverseTransport)"/>.
/// </para>
/// <para>
/// <see cref="SendAsync"/> is the only operation. The JSON conveniences
/// (<see cref="DataverseTransportExtensions.ExecuteRequestAsync(IDataverseTransport, HttpMethod, string, System.Text.Json.Nodes.JsonObject?, IDictionary{string, string}?, CancellationToken)"/>
/// and its typed overload) are extension methods over it, so an implementation has one method to write.
/// </para>
/// </remarks>
public interface IDataverseTransport
{
    /// <summary>
    /// The base URL for the Dataverse Web API
    /// (e.g., https://yourorg.api.crm.dynamics.com/api/data/v9.2).
    /// </summary>
    string BaseUrl { get; }

    /// <summary>
    /// Sends a request and returns the response.
    /// </summary>
    /// <param name="request">
    /// The request. Its URL may be relative to <see cref="BaseUrl"/> or absolute (an <c>@odata.nextLink</c>).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The successful response. The caller owns it and must dispose it.</returns>
    /// <exception cref="Models.DataverseException">The request failed. Implementations must throw for an
    /// unsuccessful response rather than return it.</exception>
    Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default);
}
