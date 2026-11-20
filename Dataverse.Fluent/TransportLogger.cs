using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dataverse.Fluent;

/// <summary>A transport that has a logger the builders can write their own diagnostics to.</summary>
internal interface ITransportLogging
{
    /// <summary>The transport's logger.</summary>
    ILogger Logger { get; }
}

/// <summary>
/// Finds the logger of a transport. The library's own transports have one; a custom
/// <see cref="IDataverseTransport"/> (a fake, or a decorator) does not, and nothing is logged for it.
/// </summary>
internal static class TransportLogger
{
    public static ILogger For(IDataverseTransport transport)
        => transport is ITransportLogging logging ? logging.Logger : NullLogger.Instance;
}
