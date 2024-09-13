using Microsoft.Extensions.Options;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;
using Newtonsoft.Json.Serialization;

namespace AdGuardHomeElasticLogs;

public class AghClients(IOptionsMonitor<Config> configMonitor, ILogger<AghClients> logger)
{
    private Dictionary<string, AdGuardClient> _adGuardClients = [];

    public AdGuardClient? GetClientInfo(string clientId) =>
        _adGuardClients.TryGetValue(clientId, out var client) ? client : null;

    public async Task LoadAdGuardClients(CancellationToken ct)
    {

        var configFilePath = configMonitor.CurrentValue.AdGuardHomeConfigFile;

        if (File.Exists(configFilePath))
        {
            try
            {
                logger.LogInformation("Loading AdGuardHome clients...");
                var yamlText = await File.ReadAllTextAsync(configFilePath, ct);
                var deserializer =
                    new DeserializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .IgnoreUnmatchedProperties()
                        .Build();

                var yamlObject = deserializer.Deserialize<AdGuardHomeConfig>(yamlText);

                if (yamlObject.Clients?.Persistent is not null)
                {
                    _adGuardClients =
                        yamlObject.Clients.Persistent
                            .SelectMany(client => client.Ids.Select(id => new { Id = id, Client = client }))
                            .ToDictionary(x => x.Id, x => x.Client);
                }

                logger.LogInformation("AdGuardHome clients loaded: {count}", _adGuardClients.Count);
            }
            catch (Exception ex)
            {
                logger.LogError("Failed to load AdGuardHome YAML config: {error}", ex.Message);
                _adGuardClients = [];
            }
        }
        else
        {
            logger.LogWarning("AdGuardHome config file not found: {path}", configFilePath);
            _adGuardClients = [];
        }
    }
}

public class AdGuardHomeConfig
{
    public AdGuardClients Clients { get; set; } = null!;
}

public class AdGuardClients
{
    public List<AdGuardClient> Persistent { get; set; } = null!;
}

public class AdGuardClient
{
    public string Name { get; set; } = null!;
    public List<string> Ids { get; set; } = null!;
    public bool IgnoreQuerylog { get; set; }
    public bool IgnoreStatistics { get; set; }
}