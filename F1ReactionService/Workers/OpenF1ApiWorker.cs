using F1ReactionService.Data;
using F1ReactionService.Model;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;

namespace F1ReactionService.Workers;

/// <summary>
/// Provides a background service that automatically synchronizes completed F1 sessions 
/// from the OpenF1 REST API into the local SQLite database for offline replays (Free Tier).
/// </summary>
public class OpenF1ApiWorker(
	IHttpClientFactory httpClientFactory,
	ILogger<OpenF1ApiWorker> logger,
	IDbContextFactory<F1DbContext> dbFactory) : BackgroundService {
	private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
	private readonly ILogger<OpenF1ApiWorker> _logger = logger;
	private readonly IDbContextFactory<F1DbContext> _dbFactory = dbFactory;
	private F1RaceAnalyzer _raceAnalyzer;

	/// <inheritdoc/>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		_logger.LogInformation("OpenF1ApiWorker (Smart Sync) started. Checking for missing sessions every 4 hours.");

		while (!stoppingToken.IsCancellationRequested) {
			try {
				await PerformSmartSyncAsync(stoppingToken);
			} catch (Exception ex) {
				_logger.LogError(ex, "Error during Smart Sync.");
			}

			_logger.LogInformation("Smart Sync complete. Sleeping for 4 hours.");
			await Task.Delay(TimeSpan.FromHours(4), stoppingToken);
		}
	}

	/// <summary>
	/// Performs a synchronization operation to import any completed sessions from the current year that are missing from
	/// the local database.
	/// </summary>
	/// <remarks>Only sessions that have ended at least two hours ago and are not already present in the local
	/// database are imported. The method logs information about each imported session and introduces a delay between
	/// imports. If the operation is canceled via the provided token, the synchronization stops promptly.</remarks>
	/// <param name="stoppingToken">A cancellation token that can be used to cancel the operation before it completes.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
	private async Task PerformSmartSyncAsync(CancellationToken stoppingToken) {
		var client = _httpClientFactory.CreateClient("OpenF1");
		int currentYear = DateTime.UtcNow.Year;

		using var db = await _dbFactory.CreateDbContextAsync(stoppingToken);
		var existingSessionKeys = await db.Sessions.Select(s => s.Id).ToListAsync(stoppingToken);

		var allSessions = await client.GetFromJsonAsync<List<JsonElement>>($"sessions?year={currentYear}", stoppingToken);
		if (allSessions == null) {
			return;
		}

		foreach (var session in allSessions) {
			if (stoppingToken.IsCancellationRequested) {
				break;
			}

			if (session.TryGetProperty("session_key", out var keyProp) &&
				session.TryGetProperty("session_name", out var nameProp) &&
				session.TryGetProperty("date_end", out var endProp)) {
				var sessionKey = keyProp.GetInt32().ToString();
				var rawSessionName = nameProp.GetString() ?? "Unknown";
				var dateEnd = endProp.GetDateTime();

				var country = session.TryGetProperty("country_name", out var cProp) ? cProp.GetString() : "";
				var circuit = session.TryGetProperty("circuit_short_name", out var cirProp) ? cirProp.GetString() : "";

				var sessionName = string.IsNullOrWhiteSpace(country)
					? rawSessionName
					: $"{country} ({circuit}) - {rawSessionName}";

				if (existingSessionKeys.Contains(sessionKey)) {
					continue;
				}

				if (DateTime.UtcNow > dateEnd.AddHours(2)) {
					_logger.LogInformation("Found missing completed session: {Name} ({Key}). Starting historical import...", sessionName, sessionKey);
					await ImportSessionDataAsync(client, sessionKey, sessionName, stoppingToken);

					_logger.LogInformation("Successfully imported session: {Name}", sessionName);
					await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
				}
			}
		}
	}

	/// <summary>
	/// Imports session-related event data from multiple endpoints and records the events for the specified session.
	/// </summary>
	/// <remarks>If an endpoint returns a 404 Not Found response, the method skips that endpoint and continues
	/// processing the remaining endpoints. The method enforces a rate limit of three requests per second to avoid
	/// overwhelming the data source.</remarks>
	/// <param name="client">The HTTP client used to send requests to the data endpoints.</param>
	/// <param name="sessionKey">The unique key identifying the session for which data is being imported. Cannot be null or empty.</param>
	/// <param name="sessionName">The display name of the session associated with the imported data.</param>
	/// <param name="stoppingToken">A cancellation token that can be used to cancel the import operation.</param>
	/// <returns>A task that represents the asynchronous import operation.</returns>
	private async Task ImportSessionDataAsync(HttpClient client, string sessionKey, string sessionName, CancellationToken stoppingToken) {
		var endpoints = new[] {
		"race_control",
		"weather",
		"pit",
		"position?position=1"
	};

		// 1. Startzeit und Session-Typ abfragen
		var sessionInfo = await client.GetFromJsonAsync<List<JsonElement>>($"sessions?session_key={sessionKey}", stoppingToken);
		if (sessionInfo == null || sessionInfo.Count == 0) {
			return;
		}

		var trueDataStartTime = sessionInfo[0].GetProperty("date_start").GetDateTime();
		var sessionType = sessionInfo[0].GetProperty("session_type").GetString();
		bool isRace = sessionType == "Race";

		_raceAnalyzer ??= new F1RaceAnalyzer();
		_raceAnalyzer.Reset();
		var drivers = await client.GetFromJsonAsync<List<OpenF1Driver>>($"drivers?session_key={sessionKey}", stoppingToken);
		if (drivers != null) {
			_raceAnalyzer.UpdateDriverRegistry(drivers);
		}

		using var db = await _dbFactory.CreateDbContextAsync(stoppingToken);

		var existingSession = await db.Sessions.FindAsync([sessionKey], stoppingToken);
		if (existingSession == null) {
			db.Sessions.Add(new Data.Models.RaceSession {
				Id = sessionKey,
				Name = sessionName,
				Season = trueDataStartTime.Year,
				StartTimeUtc = trueDataStartTime
			});

			await db.SaveChangesAsync(stoppingToken);
			_logger.LogInformation("Created new session record: {SessionName} ({SessionKey})", sessionName, sessionKey);
		}

		foreach (var endpoint in endpoints) {
			var url = $"{endpoint}{(endpoint.Contains('?') ? "&" : "?")}session_key={sessionKey}";

			try {
				var eventList = await client.GetFromJsonAsync<List<JsonElement>>(url, stoppingToken);

				if (eventList != null) {
					var cleanTopic = $"f1/{endpoint.Split('?')[0]}";

					foreach (var item in eventList) {
						if (!item.TryGetProperty("date", out var dateProp)) {
							continue;
						}

						var eventTime = dateProp.GetDateTime();
						var offsetMs = (long)(eventTime - trueDataStartTime).TotalMilliseconds;
						if (offsetMs < 0) {
							offsetMs = 0;
						}

						Model.RaceEvent? singleEvent = null;
						List<Model.RaceEvent>? multipleEvents = null;

						switch (cleanTopic) {
							case "f1/weather":
								singleEvent = _raceAnalyzer.ProcessWeather(item);
								break;
							case "f1/position":
								singleEvent = _raceAnalyzer.ProcessLeader(item, isRace, sessionName, false);
								break;
							case "f1/pit":
								// Die Methode erwartet eine Liste, wir übergeben das aktuelle Element als Liste
								multipleEvents = _raceAnalyzer.ProcessPitStops([item]);
								break;
							case "f1/race_control":
								multipleEvents = [];
								multipleEvents.AddRange(_raceAnalyzer.ProcessFlags([item]));
								multipleEvents.AddRange(_raceAnalyzer.ProcessRetirements([item]));
								multipleEvents.AddRange(_raceAnalyzer.ProcessFastestLap([item]));
								break;
						}

						if (singleEvent != null) {
							db.Events.Add(new Data.Models.RaceEvent {
								SessionId = sessionKey,
								Topic = singleEvent.Topic,
								Payload = singleEvent.Payload,
								SyncOffsetMs = offsetMs
							});
						}

						if (multipleEvents != null && multipleEvents.Count > 0) {
							foreach (var ev in multipleEvents) {
								db.Events.Add(new Data.Models.RaceEvent {
									SessionId = sessionKey,
									Topic = ev.Topic,
									Payload = ev.Payload,
									SyncOffsetMs = offsetMs
								});
							}
						}
					}

					await db.SaveChangesAsync(stoppingToken);
					_logger.LogDebug("Imported {Count} events for {Topic}.", eventList.Count, cleanTopic);
				}
			} catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) {
				_logger.LogWarning("Endpoint {Url} returned 404 Not Found. Skipping this endpoint for session {SessionKey}.", url, sessionKey);
			} catch (Exception ex) {
				_logger.LogError(ex, "Error fetching data from {Url} for session {SessionKey}.", url, sessionKey);
			}

			await Task.Delay(400, stoppingToken);
		}
	}
}