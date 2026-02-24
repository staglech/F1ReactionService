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
/// <remarks>
/// Initializes a new instance of the OpenF1Worker class with the specified HTTP client factory, event channel, logger,
/// and session state.
/// </remarks>
/// <param name="httpClientFactory">The factory used to create HTTP client instances for making external API requests. Cannot be null.</param>
/// <param name="channel">The channel used to send RaceEvent messages for processing. Cannot be null.</param>
/// <param name="logger">The logger used to record diagnostic and operational information. Cannot be null.</param>
/// <param name="sessionState">The session state object that tracks the current Formula 1 session context. Cannot be null.</param>
public class OpenF1Worker(IHttpClientFactory httpClientFactory,
	Channel<RaceEvent> channel,
	ILogger<OpenF1Worker> logger,
	F1SessionState sessionState) : BackgroundService {
	private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
	private readonly ChannelWriter<RaceEvent> _channelWriter = channel.Writer;
	private readonly ILogger<OpenF1Worker> _logger = logger;
	private readonly F1SessionState _sessionState = sessionState;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		var analyzer = new F1RaceAnalyzer();

		SessionInfo currentSessionInfo = new();
		DemoHttpMessageHandler? demoHandler = null;

		_logger.LogInformation("🏎️ OpenF1Worker started.");

		while (!stoppingToken.IsCancellationRequested) {
			if (!_sessionState.IsActive) {
				_logger.LogInformation("💤 Standby. Waiting for START signal or timer...");
				currentSessionInfo = new SessionInfo();
				demoHandler = null;
				await _sessionState.WakeUpSignal.WaitAsync(TimeSpan.FromMinutes(10), stoppingToken);
				continue;
			}

			try {
				HttpClient client;
				if (_sessionState.IsDemoMode) {
					demoHandler ??= new DemoHttpMessageHandler(_sessionState);
					client = new HttpClient(demoHandler) { BaseAddress = new Uri("https://api.openf1.org/v1/") };
				} else {
					demoHandler = null;
					client = _httpClientFactory.CreateClient("OpenF1");
				}

				// update session info
				await UpdateSessionInfo(client, currentSessionInfo, stoppingToken);

				if (currentSessionInfo.IsStale) {
					_logger.LogDebug("Last session is older than 24h. Ignoring data.");
					await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
					continue;
				}

				// update driver registry if needed
				await CheckDriverRegistry(analyzer, currentSessionInfo, client, stoppingToken);

				// check track status
				await CheckTrackStatusAsync(client, analyzer, stoppingToken);

				// check for leader chagne
				await CheckLeaderAsync(client, analyzer, currentSessionInfo, stoppingToken);

			} catch (Exception ex) {
				_logger.LogError(ex, "Error fetching data from the OpenF1 API.");
			}

			await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
		}
	}

	/// <summary>
	/// Asynchronously updates the specified session information object with the latest session data retrieved from the
	/// remote service.
	/// </summary>
	/// <remarks>This method retrieves the most recent session data and updates the provided session information
	/// object accordingly. The session is considered live if the current time is within 30 minutes after the session's end
	/// time, and stale if more than 24 hours have passed since the session ended.</remarks>
	/// <param name="client">The HTTP client used to send the request to the remote service.</param>
	/// <param name="sessionInfo">The session information object to update with the latest session details.</param>
	/// <param name="stoppingToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
	/// <returns>A task that represents the asynchronous update operation.</returns>
	private static async Task UpdateSessionInfo(HttpClient client, SessionInfo sessionInfo, CancellationToken stoppingToken) {
		var sessions = await client.GetFromJsonAsync<List<JsonElement>>("sessions?session_key=latest", stoppingToken);
		var session = sessions?.LastOrDefault();

		if (session != null && session.Value.ValueKind != JsonValueKind.Undefined) {
			sessionInfo.SessionName = session.Value.GetProperty("session_name").GetString() ?? "Unknown";
			sessionInfo.IsRace = sessionInfo.SessionName.Contains("Race", StringComparison.OrdinalIgnoreCase);

			if (session.Value.TryGetProperty("date_start", out var startProp) &&
				session.Value.TryGetProperty("date_end", out var endProp)) {
				var dateEnd = endProp.GetDateTime();
				sessionInfo.IsLive = DateTime.UtcNow >= startProp.GetDateTime() && DateTime.UtcNow <= dateEnd.AddMinutes(30);
				sessionInfo.IsStale = (DateTime.UtcNow - dateEnd).TotalHours > 24;
			}
		}
	}

	/// <summary>
	/// Checks whether the driver registry requires an update and, if necessary, fetches the latest driver lineup from the
	/// OpenF1 API and updates the analyzer's registry.
	/// </summary>
	/// <param name="analyzer">The analyzer instance whose driver registry may be updated.</param>
	/// <param name="sessionInfo">The session information used to determine if a new session has started and whether an update is needed.</param>
	/// <param name="client">The HTTP client used to retrieve driver data from the OpenF1 API.</param>
	/// <param name="stoppingToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
	private async Task CheckDriverRegistry(F1RaceAnalyzer analyzer, SessionInfo sessionInfo, HttpClient client, CancellationToken stoppingToken) {
		if (analyzer.NeedsDriverRegistryUpdate(sessionInfo.IsNewSession)) {
			_logger.LogInformation("Fetching current driver lineup live from OpenF1 API...");
			var drivers = await client.GetFromJsonAsync<List<OpenF1Driver>>("drivers?session_key=latest", stoppingToken);
			if (drivers != null && drivers.Count != 0) {
				analyzer.UpdateDriverRegistry(drivers);
				_logger.LogInformation("✅ Successfully loaded drivers into the registry.");
			}
		}
	}

	/// <summary>
	/// Checks the current track status asynchronously and publishes any detected flag change events.
	/// </summary>
	/// <remarks>This method retrieves the latest track status, processes it to detect flag changes, and publishes
	/// any detected events. If no flag change is detected, no event is published.</remarks>
	/// <param name="client">The HTTP client used to retrieve the latest track status data.</param>
	/// <param name="analyzer">The analyzer responsible for processing the track status and detecting flag change events.</param>
	/// <param name="stoppingToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
	private async Task CheckTrackStatusAsync(HttpClient client, F1RaceAnalyzer analyzer, CancellationToken stoppingToken) {
		var statusList = await client.GetFromJsonAsync<List<JsonElement>>("track_status?session_key=latest", stoppingToken);
		var flagEvent = analyzer.ProcessTrackStatus(statusList?.LastOrDefault());

		if (flagEvent != null) {
			await _channelWriter.WriteAsync(flagEvent, stoppingToken);
			_logger.LogInformation("🏁 FLAG CHANGE detected and published.");
		}
	}

	/// <summary>
	/// Checks for changes in the race leader and publishes a leader change event if detected.
	/// </summary>
	/// <remarks>This method retrieves the latest leader position, analyzes it for changes, and publishes an event
	/// if a new leader is detected. The event is written to a channel for further processing.</remarks>
	/// <param name="client">The HTTP client used to retrieve the latest leader position data.</param>
	/// <param name="analyzer">The analyzer responsible for processing leader position information and generating leader change events.</param>
	/// <param name="sessionInfo">The session information containing details about the current race session.</param>
	/// <param name="stoppingToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
	private async Task CheckLeaderAsync(HttpClient client, F1RaceAnalyzer analyzer, SessionInfo sessionInfo, CancellationToken stoppingToken) {
		var posList = await client.GetFromJsonAsync<List<JsonElement>>("position?session_key=latest&position=1", stoppingToken);

		var p1Event = analyzer.ProcessLeader(
			posList?.LastOrDefault(),
			sessionInfo.IsRace,
			sessionInfo.SessionName,
			sessionInfo.IsLive
		);

		if (p1Event != null) {
			await _channelWriter.WriteAsync(p1Event, stoppingToken);
			_logger.LogWarning("🏆 P1 CHANGE detected and published.");
		}
	}

}