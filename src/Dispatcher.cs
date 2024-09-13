using Elasticsearch.Net;
using Microsoft.Extensions.Options;
using Nest;
using Polly;
using Polly.Retry;

namespace AdGuardHomeElasticLogs;

public class Dispatcher
{
    private static readonly TimeSpan maxBackoff = TimeSpan.FromMinutes(10);
    private readonly ElasticClient _elasticClient;
    private readonly IOptionsMonitor<Config> _configMonitor;
    private readonly ILogger<Dispatcher> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    public Dispatcher(
        IOptionsMonitor<Config> configMonitor,
        ILogger<Dispatcher> logger
    )
    {
        _logger = logger;
        _configMonitor = configMonitor;

        var endpoints =
            configMonitor.CurrentValue
                .ElasticsearchEndpoints
                .Select(e => new Uri(e))
                .ToArray();

        var connectionSettings =
            endpoints.Length is 1
                ? new ConnectionSettings(endpoints.Single())
                : new ConnectionSettings(new StaticConnectionPool(endpoints));

        var settings =
            connectionSettings
                .DefaultIndex(configMonitor.CurrentValue.DefaultIndex)
                .BasicAuthentication(
                    configMonitor.CurrentValue.ElasticsearchUsername,
                    configMonitor.CurrentValue.ElasticsearchPassword
                );

        _elasticClient = new ElasticClient(settings);

        _retryPolicy = Polly.Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: int.MaxValue, // retry forever
                sleepDurationProvider: attempt =>
                {
                    var nextBackoff = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    if (nextBackoff > maxBackoff) return maxBackoff;
                    return nextBackoff;
                },
                onRetry: (exception, timespan, attempt, context) =>
                {
                    _logger.LogWarning(
                        "Retry {attempt} for logging due to: {error}",
                        attempt,
                        exception.Message
                    );
                }
            );
    }

    public async Task Dispatch(dynamic log, CancellationToken ct)
    {
        var timestamp     = log.timestamp;
        var indexName     = _configMonitor.CurrentValue.DefaultIndex.Replace("*", $"{timestamp:yyyy-MM-dd}");

        await _retryPolicy.ExecuteAsync(async () => {
            var indexResponse = await _elasticClient.IndexAsync(
                document: log as object,
                selector: i => i.Index(indexName),
                ct: ct
            );

            if (!indexResponse.IsValid)
            {
                _logger.LogError(
                    "Failed to index log: {error} - {debugInfo}",
                    indexResponse.OriginalException?.Message,
                    indexResponse.DebugInformation
                );
            }
        });
    }
}
