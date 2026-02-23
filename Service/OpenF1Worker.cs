using F1ReactionService.Model;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;

namespace F1ReactionService;

/// <summary>
/// Provides a background service that monitors Formula 1 session data from the OpenF1 API and publishes race events
/// such as leader changes and track status updates to a channel for downstream processing.
/// </summary>
/// <remarks>This worker periodically polls the OpenF1 API for the latest session, position, and track status
/// information. It emits events when the race leader changes or when the track status flag changes. The service remains
/// idle until activated by a session state signal or a timeout. Designed for integration with systems that consume
/// real-time Formula 1 telemetry or event data. Thread safety is managed internally, and the service is intended to run
/// for the application's lifetime.</remarks>
public class OpenF1Worker : BackgroundService {
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ChannelWriter<RaceEvent> _channelWriter;
	private readonly ILogger<OpenF1Worker> _logger;
	private readonly F1SessionState _sessionState;

	/// <summary>
	/// Initializes a new instance of the OpenF1Worker class with the specified HTTP client factory, event channel, logger,
	/// and session state.
	/// </summary>
	/// <param name="httpClientFactory">The factory used to create HTTP client instances for making external API requests. Cannot be null.</param>
	/// <param name="channel">The channel used to send RaceEvent messages for processing. Cannot be null.</param>
	/// <param name="logger">The logger used to record diagnostic and operational information. Cannot be null.</param>
	/// <param name="sessionState">The session state object that tracks the current Formula 1 session context. Cannot be null.</param>
	public OpenF1Worker(IHttpClientFactory httpClientFactory,
		Channel<RaceEvent> channel,
		ILogger<OpenF1Worker> logger,
		F1SessionState sessionState) {
		_httpClientFactory = httpClientFactory;
		_channelWriter = channel.Writer;
		_logger = logger;
		_sessionState = sessionState;
	}

	/// <summary>
	/// Executes the background worker loop that monitors OpenF1 session state, retrieves session and leader information,
	/// and publishes relevant events until cancellation is requested.
	/// </summary>
	/// <remarks>The method periodically checks the OpenF1 API for session and leader updates, and waits for either
	/// a wake-up signal or a timeout when the session is inactive. It logs status information and publishes events when
	/// the leader changes. The loop continues until the provided cancellation token is signaled.</remarks>
	/// <param name="stoppingToken">A cancellation token that can be used to request the termination of the background operation.</param>
	/// <returns>A task that represents the asynchronous execution of the background worker loop.</returns>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		var client = _httpClientFactory.CreateClient("OpenF1");
		string? lastStatus = null;
		int? lastP1Driver = null;
		string currentSessionName = "Unknown";
		bool isRace = false;

		_logger.LogInformation("🏎️ OpenF1Worker mit P1-Logik gestartet.");

		while (!stoppingToken.IsCancellationRequested) {

			if (!_sessionState.IsActive) {
				_logger.LogInformation("💤 Standby. Warte auf START-Signal oder Timer...");

				// Hier passiert die Magie:
				// Er wartet entweder bis jemand die Klingel drückt (Release)
				// ODER bis 10 Minuten um sind (Timeout)
				// ODER bis der Container gestoppt wird (stoppingToken)
				await _sessionState.WakeUpSignal.WaitAsync(TimeSpan.FromMinutes(10), stoppingToken);

				// Wenn wir hier ankommen, hat entweder jemand geklingelt oder die 10 Min sind um.
				// Wir springen an den Anfang der Schleife und prüfen 'IsActive'.
				continue;
			}

			if (_sessionState.IsDemoMode) {
				await RunDemoSequence(stoppingToken);
				continue; // Nach dem Skript fängt er wieder von vorne an, solange IsDemoMode an ist
			}

			try {
				// 1. Session Info holen (alle 30 Sek reicht hier eigentlich, aber wir machen es im Loop)
				var sessions = await client.GetFromJsonAsync<List<JsonElement>>("sessions?session_key=latest", stoppingToken);
				var session = sessions?.LastOrDefault();

				bool isLive = false;
				bool isStale = false;

				if (session != null && session.Value.ValueKind != JsonValueKind.Undefined) {
					currentSessionName = session.Value.GetProperty("session_name").GetString() ?? "Unknown";
					// Prüfen, ob es ein echtes Rennen ist
					isRace = currentSessionName.Contains("Race", StringComparison.OrdinalIgnoreCase);

					// Zeitstempel auslesen (OpenF1 liefert immer UTC)
					if (session.Value.TryGetProperty("date_start", out var startProp) &&
						session.Value.TryGetProperty("date_end", out var endProp)) {
						var dateStart = startProp.GetDateTime();
						var dateEnd = endProp.GetDateTime();
						var now = DateTime.UtcNow;

						// Ist das Event JETZT gerade live? (Mit 30 Min Puffer nach hinten für Siegerehrung)
						isLive = now >= dateStart && now <= dateEnd.AddMinutes(30);

						// Ist das Event schon völlig veraltet? (Älter als 24 Stunden)
						isStale = (now - dateEnd).TotalHours > 24;
					}
				}

				// Wenn die Daten uralt sind (wie Day 3 von letzter Woche), brechen wir hier ab!
				if (isStale) {
					_logger.LogDebug("Letzte Session ist älter als 24h. Ignoriere Daten.");
					await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
					continue; // Springt zurück an den Anfang der while-Schleife
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

							// Wir schicken ein konsistentes Paket an MQTT
							await PublishEvent("f1/race/p1", new {
								driver = driver.Name,
								driver_number = driverNum,
								short_name = driver.Abbreviation,
								team = team.Name,
								color = team.ColorHex,
								reason = isRace ? "Race Leader" : "Fastest Lap",
								session = currentSessionName,
								is_live = isLive
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

	/// <summary>
	/// Checks the latest track status and publishes an event if the status has changed.
	/// </summary>
	/// <remarks>If the track status has changed since the last check, this method updates the status and publishes
	/// a flag status event. The method does not publish an event if the status is unchanged or unavailable.</remarks>
	/// <param name="client">The HTTP client used to retrieve the latest track status from the remote service.</param>
	/// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
	/// <param name="setLastStatus">An action to update the last known status when a change is detected.</param>
	/// <param name="lastStatus">The previously recorded track status, or null if no status has been recorded.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
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

	/// <summary>
	/// Publishes an event with the specified topic and payload to the event channel asynchronously.
	/// </summary>
	/// <remarks>The event is serialized to JSON before being written to the channel. This method does not guarantee
	/// immediate delivery; the event is queued for processing.</remarks>
	/// <param name="topic">The topic name that categorizes the event. Cannot be null or empty.</param>
	/// <param name="payload">The event data to be published. The object will be serialized to JSON before publishing. Cannot be null.</param>
	/// <returns>A task that represents the asynchronous publish operation.</returns>
	private async Task PublishEvent(string topic, object payload) {
		var json = JsonSerializer.Serialize(payload);
		await _channelWriter.WriteAsync(new RaceEvent(topic, json));
	}

	/// <summary>
	/// Maps a status code to its corresponding flag name.
	/// </summary>
	/// <param name="status">The status code to map. Expected values are "1", "2", "4", "5", or "6". Other values will be mapped to "UNKNOWN".</param>
	/// <returns>A string representing the flag name corresponding to the specified status code. Returns "UNKNOWN" if the status
	/// code is not recognized.</returns>
	private static string MapFlag(string status) => status switch {
		"1" => "GREEN",
		"2" => "YELLOW",
		"4" => "SC",
		"5" => "RED",
		"6" => "VSC",
		_ => "UNKNOWN"
	};

	#region [ Demo data ]

	/// <summary>
	/// Runs a demonstration sequence that simulates a series of race events by publishing status updates and delays
	/// between scenes.
	/// </summary>
	/// <remarks>This method is intended for demonstration or testing purposes and simulates race scenarios by
	/// publishing events and introducing delays. The sequence can be interrupted by cancelling the provided
	/// token.</remarks>
	/// <param name="ct">A cancellation token that can be used to cancel the demo sequence before completion.</param>
	/// <returns>A task that represents the asynchronous operation of running the demo sequence.</returns>
	private async Task RunDemoSequence(CancellationToken ct) {
		_logger.LogInformation("🎬 Starte Demo-Szene 1: Rennstart (Grün & Red Bull führt)");
		await PublishEvent("f1/race/flag_status", new { FLAG = "GREEN", MESSAGE = "Track Clear" });
		await PublishEvent("f1/race/p1", new { driver = "Max Verstappen", driver_number = 1, short_name = "VER", team = "Red Bull Racing", color = "#4781D7", reason = "Race Leader", session = "Race", is_live = true });
		await Task.Delay(8000, ct);

		_logger.LogInformation("🎬 Starte Demo-Szene 2: Gelbe Flagge Sektor 2");
		await PublishEvent("f1/race/flag_status", new { FLAG = "YELLOW", MESSAGE = "Yellow in Sector 2" });
		await Task.Delay(5000, ct);

		_logger.LogInformation("🎬 Starte Demo-Szene 3: Safety Car & McLaren übernimmt Führung");
		await PublishEvent("f1/race/flag_status", new { FLAG = "SC", MESSAGE = "Safety Car Deployed" });
		await PublishEvent("f1/race/p1", new { driver = "Lando Norris", driver_number = 4, short_name = "NOR", team = "McLaren", color = "#F47600", reason = "Race Leader", session = "Race", is_live = true });
		await Task.Delay(8000, ct);

		_logger.LogInformation("🎬 Starte Demo-Szene 4: Rote Flagge");
		await PublishEvent("f1/race/flag_status", new { FLAG = "RED", MESSAGE = "Session Suspended" });
		await Task.Delay(5000, ct);

		_logger.LogInformation("🎬 Starte Demo-Szene 5: Restart & Ferrari führt");
		await PublishEvent("f1/race/flag_status", new { FLAG = "GREEN", MESSAGE = "Track Clear" });
		await PublishEvent("f1/race/p1", new { driver = "Lewis Hamilton", driver_number = 44, short_name = "HAM", team = "Ferrari", color = "#ED1131", reason = "Race Leader", session = "Race", is_live = true });
		await Task.Delay(8000, ct);

		_logger.LogInformation("🏁 Demo-Durchlauf beendet. Pause für 10 Sekunden, dann Neustart...");
		await Task.Delay(10000, ct);
	}

	#endregion [ Demo data ]
}