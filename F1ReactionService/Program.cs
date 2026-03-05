using F1ReactionService;
using F1ReactionService.Data;
using F1ReactionService.Model;
using F1ReactionService.Recording;
using F1ReactionService.Workers;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Threading.Channels;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<IDbContextFactory<F1DbContext>, DynamicF1DbContextFactory>();
builder.Logging.ClearProviders();

builder.Logging.AddSimpleConsole(options => {
	options.TimestampFormat = "[dd.MM.yyyy HH:mm:ss] ";
	options.SingleLine = true;
});

builder.Services.AddHttpClient("OpenF1", client => {
	client.BaseAddress = new Uri("https://api.openf1.org/v1/");
});

builder.Services.AddSingleton<F1SessionState>();
builder.Services.AddSingleton<IMqttCommandProcessor, MqttCommandProcessor>();
builder.Services.AddSingleton(Channel.CreateUnbounded<RaceEvent>());
builder.Services.AddSingleton<F1EventRecorder>();

builder.Services.AddHostedService<OpenF1Worker>();
builder.Services.AddHostedService<MqttWorker>();
builder.Services.AddHostedService<F1ReplayWorker>();

var host = builder.Build();

var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "Unknown";
var logger = host.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("=========================================");
logger.LogInformation(" 🏎️ F1 REACTION SERVICE - VERSION {Version}", version);
logger.LogInformation("=========================================");

await host.RunAsync();