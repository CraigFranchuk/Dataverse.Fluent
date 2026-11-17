using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Dataverse.Fluent;

/// <summary>
/// Centralized OpenTelemetry instrumentation for the Dataverse client.
/// Consumers can subscribe via the well-known source/meter names to collect
/// distributed traces and metrics without any runtime coupling to OpenTelemetry SDKs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tracing</b> — subscribe to <see cref="ActivitySourceName"/>:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t => t.AddSource(DataverseTelemetry.ActivitySourceName));
/// </code>
/// </para>
/// <para>
/// <b>Metrics</b> — subscribe to <see cref="MeterName"/>:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithMetrics(m => m.AddMeter(DataverseTelemetry.MeterName));
/// </code>
/// </para>
/// </remarks>
public static class DataverseTelemetry
{
    /// <summary>
    /// The name of the <see cref="ActivitySource"/> used for distributed tracing.
    /// </summary>
    public const string ActivitySourceName = "Dataverse.Fluent";

    /// <summary>
    /// The name of the <see cref="Meter"/> used for request metrics.
    /// </summary>
    public const string MeterName = "Dataverse.Fluent";

    internal static readonly ActivitySource Source = new(ActivitySourceName, GetVersion());

    private static readonly Meter MeterInstance = new(MeterName, GetVersion());

    /// <summary>
    /// Total number of Dataverse HTTP requests made.
    /// </summary>
    internal static readonly Counter<long> RequestCount =
        MeterInstance.CreateCounter<long>(
            "dataverse.client.requests",
            unit: "{request}",
            description: "Total number of Dataverse HTTP requests");

    /// <summary>
    /// Duration of Dataverse HTTP requests in milliseconds.
    /// </summary>
    internal static readonly Histogram<double> RequestDuration =
        MeterInstance.CreateHistogram<double>(
            "dataverse.client.duration",
            unit: "ms",
            description: "Duration of Dataverse HTTP requests");

    /// <summary>
    /// Total number of failed Dataverse HTTP requests (non-2xx responses).
    /// </summary>
    internal static readonly Counter<long> ErrorCount =
        MeterInstance.CreateCounter<long>(
            "dataverse.client.errors",
            unit: "{request}",
            description: "Total number of failed Dataverse HTTP requests");

    private static string GetVersion()
        => typeof(DataverseTelemetry).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}
