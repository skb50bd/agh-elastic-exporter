using ARSoft.Tools.Net.Dns;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace AdGuardHomeElasticLogs;

public class AghQuerylogProcessor(
    ILogger<AghQuerylogProcessor> logger,
    AghClients aghClients,
    PtrClients ptrClients,
    IOptionsMonitor<Config> configMonitor
)
{
    public async Task<dynamic?> Deserialize(string json, CancellationToken ct)
    {
        var jsonLog = JObject.Parse(json);

        var base64Answer = jsonLog["Answer"]?.ToString();
        if (base64Answer is null)
        {
            return null;
        }

        try
        {
            var dnsBytes        = Convert.FromBase64String(base64Answer);
            var dnsResponse     = DnsMessage.Parse(dnsBytes);
            var timestamp       = jsonLog["T"]?.ToObject<DateTimeOffset>();
            var elapsed         = jsonLog["Elapsed"]?.ToObject<long>() ?? 0;
            var elapsedTimespan = TimeSpan.FromTicks(elapsed);
            var clientIp        = jsonLog["IP"]?.ToString();
            var aghClientInfo   = aghClients.GetClientInfo(clientIp!);
            var clientProtocol  = jsonLog["CP"]?.ToString();
            if (string.IsNullOrEmpty(clientProtocol))
            {
                clientProtocol = "dns";
            }

            var ecsLog = new
            {
                timestamp = timestamp,
                dns = new
                {
                    question = new
                    {
                        name   = jsonLog["QH"]?.ToString(),
                        type   = jsonLog["QT"]?.ToString(),
                        @class = jsonLog["QC"]?.ToString()
                    },
                    answers = dnsResponse.AnswerRecords.Select(r => new
                    {
                        @type  = r.RecordType.ToString(),
                        name   = r.Name.ToString(),
                        ttl    = r.TimeToLive,
                        @class = r.RecordClass.ToString()
                    }).ToList(),
                    authorityRecords = dnsResponse.AuthorityRecords.Select(r => new
                    {
                        @type  = r.RecordType.ToString(),
                        name   = r.Name.ToString(),
                        ttl    = r.TimeToLive,
                        @class = r.RecordClass.ToString()
                    }).ToList(),
                    additionalRecords = dnsResponse.AdditionalRecords.Select(r => new
                    {
                        @type  = r.RecordType.ToString(),
                        name   = r.Name.ToString(),
                        ttl    = r.TimeToLive,
                        @class = r.RecordClass.ToString()
                    }).ToList(),
                    elapsed   = elapsed,
                    elapsedMs = elapsedTimespan.TotalMilliseconds
                },
                client = new
                {
                    ip                  = clientIp,
                    name                = aghClientInfo?.Name ?? await ptrClients.LookupHostname(clientIp, ct),
                    isAdGuardHomeClient = aghClientInfo is not null
                },
                network = new
                {
                    transport = clientProtocol is "dns" or "doq" ? "udp" : "tcp",
                    protocol  = clientProtocol
                },
                upstream              = jsonLog["Upstream"]?.ToString(),
                isServedFromCache     = jsonLog["Cache"]?.ToObject<bool>() ?? false,
                isEdnsEnabled         = dnsResponse.IsEDnsEnabled,
                isCheckingEnabled     = dnsResponse.IsCheckingDisabled,
                isAuthenticData       = dnsResponse.IsAuthenticData,
                isRecursionDesired    = dnsResponse.IsRecursionDesired,
                isRecursionAvailable  = dnsResponse.IsRecursionAllowed,
                isTruncated           = dnsResponse.IsTruncated,
                isAuthoritativeAnswer = dnsResponse.IsAuthoritiveAnswer,
                isDnsSecOk            = dnsResponse.IsDnsSecOk,
                operationCode         = dnsResponse.OperationCode.ToString(),
                returnCode            = dnsResponse.ReturnCode.ToString(),
                instance              = configMonitor.CurrentValue.InstanceName
            };

            return ecsLog;
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to decode DNS message: {error}", ex.Message);
            return null;
        }
    }
}
