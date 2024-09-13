using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace AdGuardHomeElasticLogs;

public class AghQuerylogsDispatcher(
    AghQuerylogProcessor aghQuerylogProcessor,
    Dispatcher dispatcher,
    ILogger<Worker> logger,
    IOptionsMonitor<Config> configMonitor
)
{
    private Timer? _checkpointTimer;
    private IDictionary<string, long> _checkpoints = new ConcurrentDictionary<string, long>();
    private readonly SemaphoreSlim _saveCheckpointSemaphore = new(1, 1);

    public async Task Run(CancellationToken stoppingToken)
    {
        _checkpoints = await LoadCheckpoints(stoppingToken);
        StartCheckpointTimer(stoppingToken);

        var logFiles = Directory.GetFiles(
            configMonitor.CurrentValue.LogDirectory,
            configMonitor.CurrentValue.LogFilePattern
        );

        logger.LogInformation("Found {count} log files to process.", logFiles.Length);
        foreach ( var logFile in logFiles ) {
            logger.LogInformation("Log file: {logFile}", logFile);
        }

        foreach (var logFilePath in logFiles)
        {
            var lastPosition =
                _checkpoints.TryGetValue(logFilePath, out long value)
                    ? value
                    : 0;

            await ProcessLogFile(logFilePath, lastPosition, stoppingToken);
        }

        await SaveCheckpoints(stoppingToken);
        StopCheckpointTimer();
    }

    private async Task ProcessLogFile(
        string logFilePath,
        long lastPosition,
        CancellationToken stoppingToken
    )
    {
        if (File.Exists(logFilePath) is false)
        {
            logger.LogError("Log file not found: {path}", logFilePath);
            return;
        }

        logger.LogInformation(
            "Processing log file: {logFilePath} from position {lastPosition}",
            logFilePath,
            lastPosition
        );

        await using var fileStream = new FileStream(logFilePath, new FileStreamOptions
        {
            Access  = FileAccess.Read,
            Share   = FileShare.ReadWrite,
            Mode    = FileMode.Open,
            Options = FileOptions.Asynchronous
        });

        using var reader = new StreamReader(fileStream);
        fileStream.Seek(lastPosition, SeekOrigin.Begin);

        string? line;
        while ((line = await reader.ReadLineAsync(stoppingToken)) is not null)
        {
            var log = await aghQuerylogProcessor.Deserialize(line, stoppingToken);
            if (log is null)
            {
                logger.LogWarning("Failed to deserialize log: {line}", line);
                continue;
            }

            await dispatcher.Dispatch(log, stoppingToken);
            _checkpoints[logFilePath] = fileStream.Position;
        }

        logger.LogInformation(
            "Finished processing log file: {logFilePath}. Read {bytesRead} bytes",
            logFilePath,
            fileStream.Position - lastPosition
        );
    }

    private async Task<IDictionary<string, long>> LoadCheckpoints(CancellationToken stoppingToken)
    {
        var checkpoints = new ConcurrentDictionary<string, long>();

        if (File.Exists(configMonitor.CurrentValue.CheckpointFile))
        {
            foreach (var line in await File.ReadAllLinesAsync(configMonitor.CurrentValue.CheckpointFile, stoppingToken))
            {
                var parts = line.Split('|');
                if (parts.Length is 2 && long.TryParse(parts[1], out var position))
                {
                    checkpoints[parts[0]] = position;
                }
            }
        }

        return checkpoints;
    }

    private async Task SaveCheckpoints(CancellationToken stoppingToken)
    {
        await _saveCheckpointSemaphore.WaitAsync(stoppingToken);
        try
        {
            await using var writer = new StreamWriter(configMonitor.CurrentValue.CheckpointFile);
            foreach (var entry in _checkpoints)
            {
                await writer.WriteLineAsync(
                    $"{entry.Key}|{entry.Value}".AsMemory(),
                    stoppingToken
                );
            }

            logger.LogInformation("Checkpoints successfully saved.");
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to save checkpoints: {error}", ex.Message);
        }
        finally
        {
            _saveCheckpointSemaphore.Release();
        }
    }

    private void StartCheckpointTimer(CancellationToken stoppingToken)
    {
        var saveInterval = TimeSpan.FromMinutes(1);
        _checkpointTimer = new Timer(
            callback : async _ => await SaveCheckpoints(stoppingToken),
            state    : null,
            dueTime  : saveInterval,
            period   : saveInterval
        );
    }

    private void StopCheckpointTimer()
    {
        _checkpointTimer?.Dispose();
        _checkpointTimer = null;
    }
}
