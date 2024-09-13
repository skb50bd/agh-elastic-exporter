namespace AdGuardHomeElasticLogs;

public class Config
{
    public string InstanceName { get; set; } = null!;
    public string[] ElasticsearchEndpoints { get; set; } = [];
    public string ElasticsearchUsername { get; set; } = null!;
    public string ElasticsearchPassword { get; set; } = null!;
    public string DefaultIndex { get; set; } = null!;
    public string LogDirectory { get; set; } = null!;
    public string LogFilePattern { get; set; } = null!;
    public string CheckpointFile { get; set; } = null!;
    public TimeSpan PollingInterval { get; set; }
    public string? AdGuardHomeConfigFile { get; set; } = null!;
}
