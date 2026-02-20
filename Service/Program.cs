using F1ReactionService;
using System.Threading.Channels;

var builder = Host.CreateApplicationBuilder(args);

// HTTP Client für OpenF1 registrieren
builder.Services.AddHttpClient("OpenF1", client => {
	client.BaseAddress = new Uri("https://api.openf1.org/v1/");
});

// Das Rohrradpost-System (Singleton)
builder.Services.AddSingleton(Channel.CreateUnbounded<RaceEvent>());

// Die beiden Worker starten
builder.Services.AddHostedService<OpenF1Worker>(); // Der Produzent
builder.Services.AddHostedService<MqttWorker>();   // Der Konsument

var host = builder.Build();
await host.RunAsync();