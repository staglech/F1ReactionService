using F1ReactionService.Data;
using F1ReactionService.Model;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Threading.Channels;

namespace F1ReactionService.Workers;

/// <summary>
/// Background service that replays recorded Formula 1 session events from the database and publishes them to a channel
/// when replay mode is activated.
/// </summary>
/// <remarks>Replay is triggered when replay mode is enabled in the session state. The worker loads events for the
/// specified session from the database and publishes them in chronological order, respecting timing offsets. Only
/// events for currently tracked drivers are published during replay. The service is designed to be responsive to
/// cancellation requests and changes in replay mode state.</remarks>
/// <param name="dbFactory">The factory used to create instances of the database context for accessing recorded session events.</param>
/// <param name="channel">The channel to which replayed race events are published for downstream processing.</param>
/// <param name="sessionState">The session state object that controls and tracks the current replay mode and session information.</param>
/// <param name="logger">The logger used to record informational, warning, and error messages during replay operations.</param>
public class F1ReplayWorker(
	IDbContextFactory<F1DbContext> dbFactory,
	Channel<RaceEvent> channel,
	F1SessionState sessionState,
	ILogger<F1ReplayWorker> logger) : BackgroundService {

	/// <summary>
	/// Executes the background replay worker operation, monitoring for and processing replay commands until cancellation
	/// is requested.
	/// </summary>
	/// <remarks>This method runs continuously, waiting for replay mode to be activated. When replay mode is enabled
	/// and a valid session ID is provided, it initiates the replay process for the specified session. The method handles
	/// cancellation and errors gracefully, ensuring that resources are cleaned up when replay completes or is
	/// interrupted.</remarks>
	/// <param name="stoppingToken">A cancellation token that can be used to request the operation to stop. The method monitors this token and
	/// terminates execution when cancellation is signaled.</param>
	/// <returns>A task that represents the asynchronous execution of the replay worker. The task completes when the operation is
	/// stopped or cancelled.</returns>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		logger.LogInformation("F1ReplayWorker started. Waiting for replay commands...");

		while (!stoppingToken.IsCancellationRequested) {
			if (!sessionState.IsReplayMode || string.IsNullOrEmpty(sessionState.ReplaySessionId)) {
				await Task.Delay(1000, stoppingToken);
				continue;
			}

			string sessionId = sessionState.ReplaySessionId;
			logger.LogInformation("Starting Replay for Session {SessionId}", sessionId);

			try {
				await RunReplayAsync(sessionId, stoppingToken);
			} catch (OperationCanceledException) {
				logger.LogInformation("Replay for Session {SessionId} was stopped/cancelled.", sessionId);
			} catch (Exception ex) {
				logger.LogError(ex, "Error during replay of session {SessionId}.", sessionId);
			} finally {
				sessionState.IsReplayMode = false;
				sessionState.ReplaySessionId = null;
			}
		}
	}

	/// <summary>
	/// Replays all events for the specified session in chronological order, publishing them to the output channel with
	/// original timing.
	/// </summary>
	/// <remarks>If no events are found for the specified session, the method logs a warning and returns without
	/// performing a replay. The replay respects driver tracking filters, allowing dynamic inclusion or exclusion of
	/// drivers during playback. The method logs progress and completion information for monitoring purposes.</remarks>
	/// <param name="sessionId">The unique identifier of the session whose events are to be replayed.</param>
	/// <param name="stoppingToken">A cancellation token that can be used to cancel the replay operation.</param>
	/// <returns>A task that represents the asynchronous replay operation.</returns>
	/// <exception cref="OperationCanceledException">Thrown if the replay is cancelled by the user or if the cancellation token is triggered during execution.</exception>
	private async Task RunReplayAsync(string sessionId, CancellationToken stoppingToken) {
		using var db = dbFactory.CreateDbContext();

		// 1. Alle Events chronologisch aus der Datenbank laden
		var events = await db.Events
			.Where(e => e.SessionId == sessionId)
			.OrderBy(e => e.SyncOffsetMs)
			.ToListAsync(stoppingToken);

		if (events.Count == 0) {
			logger.LogWarning("No events found for session {SessionId} in database.", sessionId);
			return;
		}

		logger.LogInformation("Loaded {Count} events for replay. Starting playback...", events.Count);

		var stopwatch = Stopwatch.StartNew();
		foreach (var ev in events) {
			if (!sessionState.IsReplayMode || stoppingToken.IsCancellationRequested) {
				throw new OperationCanceledException("Replay cancelled by user.");
			}

			var delayMs = ev.SyncOffsetMs - stopwatch.ElapsedMilliseconds;

			if (delayMs > 0) {
				await Task.Delay(TimeSpan.FromMilliseconds(delayMs), stoppingToken);
			}

			var raceEvent = new RaceEvent(ev.Topic, ev.Payload);

			bool shouldPublish = true;
			if (raceEvent.Topic.StartsWith("f1/driver/")) {
				var topicParts = raceEvent.Topic.Split('/');
				if (topicParts.Length >= 3 && int.TryParse(topicParts[2], out int driverNum)) {
					shouldPublish = sessionState.TrackedDrivers.ContainsKey(driverNum);
				}
			}

			if (shouldPublish) {
				await channel.Writer.WriteAsync(raceEvent, stoppingToken);
				logger.LogDebug("Replay Published: {Topic}", raceEvent.Topic);
			}
		}

		stopwatch.Stop();
		logger.LogInformation("Replay for Session {SessionId} finished successfully.", sessionId);
	}
}