using F1ReactionService;
using F1ReactionService.Model;
using System.Threading.Channels;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient("OpenF1", client => {
	client.BaseAddress = new Uri("https://api.openf1.org/v1/");
});

builder.Services.AddSingleton(Channel.CreateUnbounded<RaceEvent>());

builder.Services.AddHostedService<OpenF1Worker>();
builder.Services.AddHostedService<MqttWorker>();

var host = builder.Build();
await host.RunAsync();