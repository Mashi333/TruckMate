using WeighAgent;

var builder = Host.CreateApplicationBuilder(args);

// Runs as a Windows Service when installed as one; a normal console app otherwise (also on macOS).
builder.Services.AddWindowsService(options => options.ServiceName = "WeighAgent");

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<LatestReading>();
builder.Services.AddHostedService<SerialReaderWorker>();
builder.Services.AddHostedService<PosterWorker>();

builder.Build().Run();
