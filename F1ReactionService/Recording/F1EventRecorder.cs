using F1ReactionService.Data;
using F1ReactionService.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace F1ReactionService.Recording;

/// <summary>
/// Provides functionality to record Formula 1 event data into a database, managing event sessions and persisting event
/// information with timing offsets.
/// </summary>
/// <remarks>This class is intended for use in applications that need to capture and store time-synchronized event
/// data for Formula 1 sessions. It manages session lifecycle and ensures that event timing is consistent within each
/// session. The class is not thread-safe; concurrent usage should be externally synchronized if required.</remarks>
/// <param name="dbFactory">A factory used to create instances of the database context for persisting event and session data.</param>
/// <param name="logger">The logger used to record informational and diagnostic messages related to event recording operations.</param>
public class F1EventRecorder(
	IDbContextFactory<F1DbContext> dbFactory,
	ILogger<F1EventRecorder> logger) {
	private string? _currentSessionId;
	private DateTimeOffset _sessionStartTime;

	/// <summary>
	/// Asynchronously records an event for the specified session, including topic and payload information.
	/// </summary>
	/// <remarks>If a new session is detected based on the session identifier, the method initializes session
	/// tracking and persists the session information before recording the event. The event's timestamp is stored as an
	/// offset in milliseconds from the session start time.</remarks>
	/// <param name="sessionId">The unique identifier of the session to which the event belongs. If this value differs from the current session, a
	/// new session is started.</param>
	/// <param name="sessionName">The display name of the session. Used when starting a new session if the session identifier is new.</param>
	/// <param name="topic">The topic or category of the event being recorded. Used to classify the event within the session.</param>
	/// <param name="payload">The event data to record. This value contains the serialized content or details of the event.</param>
	/// <returns>A task that represents the asynchronous operation. The task completes when the event has been recorded.</returns>
	public async Task RecordEventAsync(string sessionId, string sessionName, string topic, string payload) {
		// Check whether a new session has been started
		if (_currentSessionId != sessionId) {
			_currentSessionId = sessionId;
			_sessionStartTime = DateTimeOffset.UtcNow;

			await EnsureSessionExistsAsync(sessionId, sessionName);
			logger.LogInformation("Started new recording-session: {SessionName} ({SessionId})", sessionName, sessionId);
		}

		var offsetMs = (long)(DateTimeOffset.UtcNow - _sessionStartTime).TotalMilliseconds;
		using var db = dbFactory.CreateDbContext();

		var raceEvent = new RaceEvent {
			SessionId = sessionId,
			SyncOffsetMs = offsetMs,
			Topic = topic,
			Payload = payload
		};

		db.Events.Add(raceEvent);
		await db.SaveChangesAsync();
	}

	/// <summary>
	/// Ensures that a session with the specified identifier exists in the database, creating it if necessary.
	/// </summary>
	/// <remarks>If a session with the specified identifier does not exist, a new session is created and saved to
	/// the database. If the session already exists, no changes are made.</remarks>
	/// <param name="sessionId">The unique identifier for the session to check or create. Cannot be null.</param>
	/// <param name="sessionName">The name to assign to the session if a new session is created. Cannot be null.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
	private async Task EnsureSessionExistsAsync(string sessionId, string sessionName) {
		using var db = dbFactory.CreateDbContext();

		var sessionExists = await db.Sessions.AnyAsync(s => s.Id == sessionId);
		if (!sessionExists) {
			var newSession = new RaceSession {
				Id = sessionId,
				Season = DateTime.UtcNow.Year,
				Name = sessionName,
				StartTimeUtc = _sessionStartTime
			};

			db.Sessions.Add(newSession);
			await db.SaveChangesAsync();
		}
	}
}