using Microsoft.Extensions.Options;

namespace AdGuardHomeElasticLogs;

public class Worker(
    AghQuerylogsDispatcher aghQuerylogsDispatcher,
    AghClients aghClients,
    ILogger<Worker> logger,
    IOptionsMonitor<Config> configMonitor
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Processing Logs at: {time}", DateTimeOffset.Now);
            await aghClients.LoadAdGuardClients(stoppingToken);
            await aghQuerylogsDispatcher.Run(stoppingToken);
            await Task.Delay(configMonitor.CurrentValue.PollingInterval, stoppingToken);
        }
    }
}
