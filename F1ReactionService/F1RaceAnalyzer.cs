using F1ReactionService.Model;
using System.Text.Json;

namespace F1ReactionService;

/// <summary>
/// Provides methods for analyzing Formula 1 race data, including driver registry management and event generation based
/// on session and track status.
/// </summary>
/// <remarks>This class is designed to facilitate the processing of live and historical Formula 1 race
/// information. It manages a registry of drivers and generates events for changes in track status and race leaders. The
/// class is not thread-safe; concurrent access should be synchronized externally if used in multi-threaded
/// scenarios.</remarks>
public class F1RaceAnalyzer {
	private string? _lastStatus;
	private int? _lastP1Driver;
	private readonly Dictionary<int, OpenF1Driver> _driverRegistry = [];

	/// <summary>
	/// Determines whether the driver registry requires an update based on the current session state.
	/// </summary>
	/// <param name="isNewSession">true to indicate that a new session has started; otherwise, false.</param>
	/// <returns>true if the driver registry is empty or a new session has started; otherwise, false.</returns>
	public bool NeedsDriverRegistryUpdate(bool isNewSession)
		=> _driverRegistry.Count == 0 || isNewSession;

	/// <summary>
	/// Updates the driver registry with the specified collection of drivers, replacing any existing entries.
	/// </summary>
	/// <remarks>Existing entries in the registry are cleared before adding the new drivers. If multiple drivers in
	/// the collection share the same driver number, the last one encountered will be stored.</remarks>
	/// <param name="drivers">An enumerable collection of drivers to add or update in the registry. Each driver's number is used as the unique
	/// key.</param>
	public void UpdateDriverRegistry(IEnumerable<OpenF1Driver> drivers) {
		_driverRegistry.Clear();
		foreach (var d in drivers) {
			_driverRegistry[d.DriverNumber] = d;
		}
	}

	/// <summary>
	/// Processes the current track status and generates a new race event if the status has changed.
	/// </summary>
	/// <remarks>This method compares the provided track status with the last known status. If the status has
	/// changed, it creates a new race event containing the updated flag and message information. If the status has not
	/// changed or the input is null or undefined, the method returns null.</remarks>
	/// <param name="currentTrackStatus">A JSON element representing the current track status. Must contain a 'status' property. If null or undefined, no
	/// event is generated.</param>
	/// <returns>A new RaceEvent instance if the track status has changed; otherwise, null.</returns>
	public RaceEvent? ProcessTrackStatus(JsonElement? currentTrackStatus) {
		if (currentTrackStatus == null || currentTrackStatus.Value.ValueKind == JsonValueKind.Undefined) {
			return null;
		}

		var status = currentTrackStatus.Value.GetProperty("status").GetString();

		// Has the track status changed since the last check?
		if (status != _lastStatus && status != null) {
			_lastStatus = status;
			return new RaceEvent("f1/race/flag_status", JsonSerializer.Serialize(new {
				flag = MapFlag(status),
				message = currentTrackStatus.Value.GetProperty("message").GetString()
			}));
		}

		return null; // nothing happens
	}

	/// <summary>
	/// Processes leader change events based on the provided driver data and session context.
	/// </summary>
	/// <remarks>A new event is only generated if the leader has changed since the last call. If the driver data
	/// does not correspond to a known driver, no event is returned.</remarks>
	/// <param name="p1Data">The JSON element containing data for the current leader. Must not be null or undefined.</param>
	/// <param name="isRace">true if the session is a race; otherwise, false. Determines the reason for the event.</param>
	/// <param name="currentSessionName">The name of the current session. Used to annotate the event data.</param>
	/// <param name="isLive">true if the session is live; otherwise, false. Indicates the live status in the event data.</param>
	/// <returns>A RaceEvent representing a leader change or fastest lap event if a new leader is detected and driver information is
	/// available; otherwise, null.</returns>
	public RaceEvent? ProcessLeader(JsonElement? p1Data, bool isRace, string currentSessionName, bool isLive) {
		if (p1Data == null || p1Data.Value.ValueKind == JsonValueKind.Undefined) {
			return null;
		}

		int driverNum = p1Data.Value.GetProperty("driver_number").GetInt32();

		// Has the leader changed since the last check?
		if (driverNum != _lastP1Driver) {
			_lastP1Driver = driverNum;

			if (_driverRegistry.TryGetValue(driverNum, out var driver)) {
				return new RaceEvent("f1/race/p1", JsonSerializer.Serialize(new {
					driver = driver.FullName,
					driver_number = driverNum,
					short_name = driver.NameAcronym,
					team = driver.TeamName,
					color = driver.TeamColour,
					reason = isRace ? "Race Leader" : "Fastest Lap",
					session = currentSessionName,
					is_live = isLive
				}));
			}
		}
		return null; // no change in leader or driver not found
	}

	/// <summary>
	/// Maps a status code to its corresponding flag name.
	/// </summary>
	/// <param name="status">The status code to map. Expected values are "1", "2", "4", "5", or "6". Other values will be mapped to "UNKNOWN".</param>
	/// <returns>A string representing the flag name corresponding to the specified status code. Returns "UNKNOWN" if the status
	/// code is not recognized.</returns>
	internal static string MapFlag(string status) => status switch {
		"1" => "GREEN",
		"2" => "YELLOW",
		"4" => "SC",
		"5" => "RED",
		"6" => "VSC",
		_ => "UNKNOWN"
	};
}