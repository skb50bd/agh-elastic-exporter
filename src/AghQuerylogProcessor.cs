using ARSoft.Tools.Net.Dns;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

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
        var sw = Stopwatch.StartNew();
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

            var resultField = jsonLog["Result"];
            var filterResult =
                resultField is null ? null : ParseFilteringResult(resultField);

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
                isServedFromCache     = jsonLog["Cached"]?.ToObject<bool>() ?? false,
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
                filteringResult       = filterResult,
                instance              = configMonitor.CurrentValue.InstanceName
            };

            sw.Stop();
            logger.LogTrace(
                "Deserialized DNS message in {elapsed} ms",
                sw.ElapsedMilliseconds
            );

            return ecsLog;
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to decode DNS message: {error}", ex.Message);
            return null;
        }
    }

    private readonly Dictionary<int, string> _reasonMap = new()
    {
        { 0,  "NotFilteredNotFound" },
        { 1,  "NotFilteredAllowList" },
        { 2,  "NotFilteredError" },
        { 3,  "FilteredBlockList" },
        { 4,  "FilteredSafeBrowsing" },
        { 5,  "FilteredParental" },
        { 6,  "FilteredInvalid" },
        { 7,  "FilteredSafeSearch" },
        { 8,  "FilteredBlockedService" },
        { 9,  "Rewritten" },
        { 10, "RewrittenAutoHosts" },
        { 11, "RewrittenRule" }
    };

    private dynamic? ParseFilteringResult(JToken resultField)
    {
        try
        {
            var rules = resultField["Rules"]?.Select(rule => new
            {
                text         = rule["Text"]?.ToString(),
                ip           = rule["IP"]?.ToString(),
                filterListId = rule["FilterListID"]?.ToObject<int>()
            }).ToList();

            var reason       = resultField["Reason"]?.ToObject<int>() ?? 0;
            var reasonString = _reasonMap.GetValueOrDefault(reason, reason.ToString());
            var isFiltered   = resultField["IsFiltered"]?.ToObject<bool>() ?? false;

            return new
            {
                rules      = rules,
                reason     = reasonString,
                isFiltered = isFiltered
            };
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to parse filtering result: {error}", ex.Message);
            return null;
        }
    }
}
