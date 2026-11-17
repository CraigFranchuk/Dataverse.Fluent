namespace Dataverse.Fluent;

/// <summary>
/// Configuration options for Dataverse-specific HTTP resilience behavior.
/// These defaults are tuned for the Dataverse Web API, which enforces service-protection limits (429)
/// and can return transient 5xx errors under load.
/// </summary>
/// <remarks>
/// Retries are write-safe: GET/HEAD/OPTIONS requests are retried on any transient failure, while
/// POST, PATCH, PUT, DELETE and <c>$batch</c> requests are retried only when Dataverse is known not to
/// have processed them (HTTP 429 service-protection responses, or failures to establish the connection).
/// A write that times out or fails with a 5xx is not re-sent, because it may already have been applied.
/// </remarks>
public class DataverseResilienceOptions
{
    /// <summary>
    /// Whether the Dataverse resilience pipeline (retry, circuit breaker, timeouts) is enabled.
    /// Default: true. Set to <c>false</c> to opt out.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum number of retry attempts. Default: 3. Set to 0 to disable retries but keep the timeouts
    /// and circuit breaker.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Median delay for the first retry. Subsequent retries use exponential backoff with jitter.
    /// A <c>Retry-After</c> header from Dataverse always takes precedence. Default: 2 seconds.
    /// </summary>
    public TimeSpan MedianFirstRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Maximum total time for a request including all retry attempts and <c>Retry-After</c> waits.
    /// Default: 10 minutes, which leaves room for service-protection <c>Retry-After</c> delays.
    /// </summary>
    public TimeSpan TotalRequestTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Timeout for a single HTTP attempt. Default: 150 seconds, above the 2-minute execution limit
    /// Dataverse applies to a request, so a slow but successful operation is not cancelled client-side.
    /// </summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(150);

}
