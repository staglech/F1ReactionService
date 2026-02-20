using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;

namespace F1ReactionService;

public class OpenF1Worker : BackgroundService {
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ChannelWriter<RaceEvent> _channelWriter;
	private readonly ILogger<OpenF1Worker> _logger;

	// Speicher für Teamfarben und Fahrernamen
	private Dictionary<int, (string Team, string Color)> _driverCache = [];

	public OpenF1Worker(IHttpClientFactory httpClientFactory, Channel<RaceEvent> channel, ILogger<OpenF1Worker> logger) {
		_httpClientFactory = httpClientFactory;
		_channelWriter = channel.Writer;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		var client = _httpClientFactory.CreateClient("OpenF1");
		string? lastStatus = null;
		int? lastLeader = null;

		_logger.LogInformation("🏎️ OpenF1Worker gestartet. Lade Fahrerdaten...");

		while (!stoppingToken.IsCancellationRequested) {
			try {
				// 1. Initial oder bei Bedarf: Fahrerdaten für Farben laden
				if (!_driverCache.Any()) {
					await LoadDriverData(client, stoppingToken);
				}

				// 2. Track Status abfragen (Flaggen)
				var statusList = await client.GetFromJsonAsync<List<JsonElement>>("track_status?session_key=latest", stoppingToken);
				var current = statusList?.LastOrDefault();
				if (current.ValueKind != JsonValueKind.Undefined) {
					var status = current.GetProperty("status").GetString();
					if (status != lastStatus) {
						lastStatus = status;
						await PublishEvent("f1/race/flag_status", new { FLAG = MapFlag(status), MESSAGE = current.GetProperty("message").GetString() });
					}
				}

				// 3. Positionen abfragen (Leader)
				var posList = await client.GetFromJsonAsync<List<JsonElement>>("position?session_key=latest&position=1", stoppingToken);
				var leader = posList?.LastOrDefault();
				if (leader.ValueKind != JsonValueKind.Undefined) {
					var driverNum = leader.GetProperty("driver_number").GetInt32();
					if (driverNum != lastLeader) {
						lastLeader = driverNum;
						_driverCache.TryGetValue(driverNum, out var info);
						await PublishEvent("f1/race/leader", new {
							driver = driverNum,
							team = info.Team ?? "Unknown",
							color = info.Color ?? "#FFFFFF"
						});
					}
				}
			} catch (Exception ex) {
				_logger.LogError(ex, "Fehler beim Abruf der F1-Daten");
			}

			await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
		}
	}

	private async Task LoadDriverData(HttpClient client, CancellationToken ct) {
		var drivers = await client.GetFromJsonAsync<List<JsonElement>>("drivers?session_key=latest", ct);
		if (drivers != null) {
			foreach (var d in drivers) {
				var num = d.GetProperty("driver_number").GetInt32();
				var team = d.GetProperty("team_name").GetString() ?? "";
				var color = d.GetProperty("team_colour").GetString() ?? "";
				_driverCache[num] = (team, "#" + color);
			}
		}
	}

	private async Task PublishEvent(string topic, object payload) {
		var json = JsonSerializer.Serialize(payload);
		await _channelWriter.WriteAsync(new RaceEvent(topic, json));
		_logger.LogInformation("📡 Event verteilt: {topic}", topic);
	}

	private string MapFlag(string? status) => status switch {
		"1" => "GREEN",
		"2" => "YELLOW",
		"4" => "SC",
		"5" => "RED",
		"6" => "VSC",
		_ => "UNKNOWN"
	};
}