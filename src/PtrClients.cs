using System.Net;

namespace AdGuardHomeElasticLogs;

public class PtrClients(ILogger<PtrClients> logger)
{
    private readonly Dictionary<string, (string? hostname, DateTimeOffset timestamp)> _ptrCache = [];

    public async Task<string?> LookupHostname(string? ipAddress, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ipAddress)) return null;

        if (_ptrCache.TryGetValue(ipAddress, out var x)
            && DateTimeOffset.UtcNow.AddHours(-1) < x.timestamp
        )
        {
            return x.hostname;
        }

        try
        {
            var dnsTask = Dns.GetHostEntryAsync(ipAddress, ct);
            var waitTask = Task.Delay(1000, ct);
            var result = (await Task.WhenAny(dnsTask, waitTask)) == dnsTask ? await dnsTask : null;
            if (result is null)
            {
                logger.LogWarning("PTR lookup timed out for {ipAddress}", ipAddress);
                _ptrCache[ipAddress] = (null, DateTimeOffset.UtcNow);
                return null;
            }

            _ptrCache[ipAddress] = (result?.HostName, DateTimeOffset.UtcNow);
            return result?.HostName;
        }
        catch (Exception ex)
        {
            logger.LogError("PTR lookup failed for {ipAddress}: {error}", ipAddress, ex.Message);
            _ptrCache[ipAddress] = (null, DateTimeOffset.UtcNow);
            return null;
        }
    }
}
