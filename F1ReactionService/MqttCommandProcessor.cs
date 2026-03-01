using F1ReactionService.Model;

namespace F1ReactionService;

/// <summary>
/// Processes MQTT commands to control the F1 session state, including starting, stopping, demo mode activation,
/// calibration, and driver tracking operations.
/// </summary>
/// <remarks>This processor interprets specific command strings and updates the session state accordingly.
/// Supported commands include starting and stopping the session, toggling demo mode, calibrating timing, and managing
/// tracked drivers. Unknown commands are logged for diagnostic purposes.</remarks>
/// <param name="logger">The logger used to record informational and warning messages related to command processing.</param>
/// <param name="sessionState">The session state object that is updated in response to received commands.</param>
public class MqttCommandProcessor(
	ILogger<MqttCommandProcessor> logger,
	F1SessionState sessionState) : IMqttCommandProcessor {

	private readonly ILogger<MqttCommandProcessor> _logger = logger;
	private readonly F1SessionState _sessionState = sessionState;

	/// <inheritdoc/>
	public void ProcessCommand(string command) {
		switch (command) {
			case "START":
				_sessionState.IsDemoMode = false;
				_sessionState.IsActive = true;
				if (_sessionState.WakeUpSignal.CurrentCount == 0) {
					_sessionState.WakeUpSignal.Release();
				}
				_logger.LogWarning("🚀 F1-service awake! Start polling...");
				break;

			case "DEMO_START":
				_sessionState.IsDemoMode = true;
				_sessionState.IsActive = true;
				_logger.LogWarning("🎪 STARTED DEMO-MODE! Running demo script...");
				if (_sessionState.WakeUpSignal.CurrentCount == 0) {
					_sessionState.WakeUpSignal.Release();
				}
				break;

			case "STOP":
				_sessionState.IsDemoMode = false;
				_sessionState.IsActive = false;
				_sessionState.TrueDataStartTime = null;
				_logger.LogWarning("💤 F1-service moves to STANDBY.");
				break;

			case "CALIBRATE_START":
				if (_sessionState.TrueDataStartTime.HasValue) {
					_sessionState.CurrentDelay = DateTime.UtcNow - _sessionState.TrueDataStartTime.Value;
					_logger.LogWarning("⏱️ CALIBRATE: Delay set to {S}s.", _sessionState.CurrentDelay.TotalSeconds);
				}
				break;

			case string s when s.StartsWith("TRACK_ADD_"):
				if (int.TryParse(s.AsSpan("TRACK_ADD_".Length), out int addNum)) {
					_sessionState.TrackedDrivers.TryAdd(addNum, true);
					_logger.LogWarning("🎯 Tracking activated for driver {DriverNum}.", addNum);
				}
				break;

			case string s when s.StartsWith("TRACK_REMOVE_"):
				if (int.TryParse(s.AsSpan("TRACK_REMOVE_".Length), out int removeNum)) {
					_sessionState.TrackedDrivers.TryRemove(removeNum, out _);
					_logger.LogWarning("🛑 Tracking disabled for driver {DriverNum}.", removeNum);
				}
				break;

			case "TRACK_CLEAR":
				_sessionState.TrackedDrivers.Clear();
				_logger.LogWarning("🧹 All tracked drivers cleared.");
				break;

			default:
				if (!string.IsNullOrWhiteSpace(command)) {
					_logger.LogDebug("Received unknown command: {Command}", command);
				}
				break;
		}
	}
}
