using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Integrations.Todoist.TodoistClient;

internal static class HttpClientBuilderExtensions
{
    private const string ResiliencePipelineName = "todoist";
    private const int MaxRetryAttempts = 3;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(20);

    public static IHttpClientBuilder AddTodoistResilience(this IHttpClientBuilder builder)
    {
        var retryOptions = new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = MaxRetryAttempts,
            Delay = RetryDelay,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldRetryAfterHeader = true
        };

        retryOptions.DisableFor(HttpMethod.Delete);

        builder.AddResilienceHandler(
            ResiliencePipelineName,
            pipeline =>
            {
                pipeline.AddRetry(retryOptions);
                pipeline.AddTimeout(AttemptTimeout);
            });

        return builder;
    }
}
