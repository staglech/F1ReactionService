using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;

namespace F1ReactionService;

public class OpenF1Worker : BackgroundService {
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ChannelWriter<RaceEvent> _channelWriter;
	private readonly ILogger<OpenF1Worker> _logger;

	public OpenF1Worker(IHttpClientFactory httpClientFactory, Channel<RaceEvent> channel, ILogger<OpenF1Worker> logger) {
		_httpClientFactory = httpClientFactory;
		_channelWriter = channel.Writer;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		var client = _httpClientFactory.CreateClient("OpenF1");
		string? lastStatus = null;
		int? lastP1Driver = null;
		string currentSessionName = "Unknown";
		bool isRace = false;

		_logger.LogInformation("🏎️ OpenF1Worker mit P1-Logik gestartet.");

		while (!stoppingToken.IsCancellationRequested) {
			try {
				// 1. Session Info holen (alle 30 Sek reicht hier eigentlich, aber wir machen es im Loop)
				var sessions = await client.GetFromJsonAsync<List<JsonElement>>("sessions?session_key=latest", stoppingToken);
				var session = sessions?.LastOrDefault();
				if (session != null && session.Value.ValueKind != JsonValueKind.Undefined) {
					currentSessionName = session.Value.GetProperty("session_name").GetString() ?? "Unknown";
					// Prüfen, ob es ein echtes Rennen ist
					isRace = currentSessionName.Contains("Race", StringComparison.OrdinalIgnoreCase);
				}

				// 2. Track Status (Flaggen)
				await CheckTrackStatus(client, stoppingToken, (s) => lastStatus = s, lastStatus);

				// 3. P1 Logik (Leader oder Fastest Lap)
				// OpenF1 gibt bei position=1 praktischerweise immer den "Besten" zurück:
				// Im Rennen den Führenden, im Training/Qualy den mit der schnellsten Zeit.
				var posList = await client.GetFromJsonAsync<List<JsonElement>>("position?session_key=latest&position=1", stoppingToken);
				var p1Data = posList?.LastOrDefault();

				if (p1Data != null && p1Data.Value.ValueKind != JsonValueKind.Undefined) {
					int driverNum = p1Data.Value.GetProperty("driver_number").GetInt32();

					if (driverNum != lastP1Driver) {
						lastP1Driver = driverNum;

						if (F1Registry.Drivers.TryGetValue(driverNum, out var driver)) {
							var team = F1Registry.Teams[driver.TeamKey];

							// Wir schicken ein konsistentes Paket an HA
							await PublishEvent("f1/race/p1", new {
								driver = driver.Name,
								short_name = driver.Abbreviation,
								team = team.Name,
								color = team.ColorHex,
								reason = isRace ? "Race Leader" : "Fastest Lap",
								session = currentSessionName
							});

							_logger.LogWarning("🏆 P1 WECHSEL: {name} ({reason} in {session})",
								driver.Name, isRace ? "Leader" : "Fastest Lap", currentSessionName);
						}
					}
				}
			} catch (Exception ex) {
				_logger.LogError(ex, "Fehler beim Abruf der OpenF1 Daten");
			}

			await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
		}
	}

	private async Task CheckTrackStatus(HttpClient client, CancellationToken ct, Action<string> setLastStatus, string? lastStatus) {
		var statusList = await client.GetFromJsonAsync<List<JsonElement>>("track_status?session_key=latest", ct);
		var current = statusList?.LastOrDefault();
		if (current != null && current.Value.ValueKind != JsonValueKind.Undefined) {
			var status = current.Value.GetProperty("status").GetString();
			if (status != lastStatus && status != null) {
				setLastStatus(status);
				await PublishEvent("f1/race/flag_status", new {
					FLAG = MapFlag(status),
					MESSAGE = current.Value.GetProperty("message").GetString()
				});
			}
		}
	}

	private async Task PublishEvent(string topic, object payload) {
		var json = JsonSerializer.Serialize(payload);
		await _channelWriter.WriteAsync(new RaceEvent(topic, json));
	}

	private string MapFlag(string status) => status switch {
		"1" => "GREEN",
		"2" => "YELLOW",
		"4" => "SC",
		"5" => "RED",
		"6" => "VSC",
		_ => "UNKNOWN"
	};
}