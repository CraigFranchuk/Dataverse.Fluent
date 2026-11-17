using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Dataverse.Fluent;

/// <summary>
/// Builds the Dataverse resilience pipeline. The same definition is used by DI registrations
/// (<see cref="ServiceCollectionExtensions"/>) and by the <see cref="DataverseClient"/> constructors that
/// create their own <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// Strategy order, outermost first: total timeout → retry → circuit breaker → per-attempt timeout.
/// </remarks>
internal static class DataverseResiliencePipeline
{
    internal const string PipelineName = "dataverse";

    internal static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> builder, DataverseResilienceOptions options)
    {
        if (!options.Enabled)
            return;

        builder.AddTimeout(new HttpTimeoutStrategyOptions
        {
            Name = "TotalRequestTimeout",
            Timeout = options.TotalRequestTimeout
        });

        if (options.MaxRetryAttempts > 0)
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay = options.MedianFirstRetryDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                // Service-protection responses carry Retry-After; the server-specified delay wins.
                ShouldRetryAfterHeader = true,
                ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome, args.Context.GetRequestMessage()))
            });
        }

        builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            ShouldHandle = args => ValueTask.FromResult(IsFault(args.Outcome))
        });

        builder.AddTimeout(new HttpTimeoutStrategyOptions
        {
            Name = "AttemptTimeout",
            Timeout = options.AttemptTimeout
        });
    }

    /// <summary>
    /// Decides whether an attempt may be retried without risking a duplicate write.
    /// </summary>
    internal static bool ShouldRetry(Outcome<HttpResponseMessage> outcome, HttpRequestMessage? request)
    {
        // HTTP 429 is a service-protection rejection: Dataverse did not execute the request, so it is
        // safe to retry any method (honoring Retry-After).
        if (outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests)
            return true;

        if (request is not null && IsSafeMethod(request.Method))
            return HttpClientResiliencePredicates.IsTransient(outcome);

        // Unsafe (or unknown) method: only retry when the request never reached the server.
        return outcome.Exception is HttpRequestException
        {
            HttpRequestError: HttpRequestError.NameResolutionError
                or HttpRequestError.ConnectionError
                or HttpRequestError.SecureConnectionError
        };
    }

    /// <summary>
    /// Failures that count towards opening the circuit. Throttling (429) is expected back-pressure, not a
    /// fault, so it never opens the circuit.
    /// </summary>
    internal static bool IsFault(Outcome<HttpResponseMessage> outcome)
        => outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests
           && HttpClientResiliencePredicates.IsTransient(outcome);

    private static bool IsSafeMethod(HttpMethod method)
        => method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options;
}
