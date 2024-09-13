using AdGuardHomeElasticLogs;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<Config>(builder.Configuration.GetSection("Config"));
builder.Services.AddSingleton<AghClients>();
builder.Services.AddSingleton<PtrClients>();
builder.Services.AddSingleton<Dispatcher>();
builder.Services.AddSingleton<AghQuerylogProcessor>();
builder.Services.AddSingleton<AghQuerylogsDispatcher>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
