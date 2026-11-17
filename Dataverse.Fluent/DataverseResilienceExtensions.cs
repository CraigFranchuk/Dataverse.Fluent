using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Dataverse.Fluent;

/// <summary>
/// Extension methods for the Dataverse resilience pipeline (write-safe retry, circuit breaker, timeouts).
/// </summary>
public static class DataverseResilienceExtensions
{
    /// <summary>
    /// Configures the Dataverse resilience pipeline on an <see cref="IHttpClientBuilder"/>.
    /// <para>
    /// On a builder returned by <c>AddDataverseClient</c>/<c>AddKeyedDataverseClient</c> (which already include
    /// the pipeline), this applies <paramref name="configure"/> to that client's
    /// <see cref="DataverseClientOptions.Resilience"/> settings. On any other <see cref="HttpClient"/> builder it
    /// adds the Dataverse pipeline with the given settings.
    /// </para>
    /// <para>
    /// Retries are write-safe: GET/HEAD/OPTIONS are retried on transient failures (HTTP 408, 429, 5xx,
    /// network errors, timeouts); other methods only on HTTP 429 (honoring <c>Retry-After</c>) or when the
    /// connection could not be established.
    /// </para>
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="configure">Optional delegate to override the default <see cref="DataverseResilienceOptions"/>.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> for further chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddDataverseClient(baseUrl, tokenProvider)
    ///     .AddDataverseResilienceHandler(options =>
    ///     {
    ///         options.MaxRetryAttempts = 5;
    ///         options.TotalRequestTimeout = TimeSpan.FromMinutes(3);
    ///     });
    /// </code>
    /// </example>
    public static IHttpClientBuilder AddDataverseResilienceHandler(
        this IHttpClientBuilder builder,
        Action<DataverseResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var registration = builder.Services
            .Select(d => d.ImplementationInstance)
            .OfType<DataverseClientRegistration>()
            .FirstOrDefault(r => r.HttpClientName == builder.Name);

        if (registration is not null)
        {
            // The Dataverse client already has the pipeline; adding a second one would stack retries.
            if (configure is not null)
                builder.Services.Configure<DataverseClientOptions>(registration.Name, o => configure(o.Resilience));
            return builder;
        }

        var options = new DataverseResilienceOptions();
        configure?.Invoke(options);
        if (options.Enabled)
            builder.AddResilienceHandler(DataverseResiliencePipeline.PipelineName, pipeline => DataverseResiliencePipeline.Configure(pipeline, options));

        return builder;
    }
}
