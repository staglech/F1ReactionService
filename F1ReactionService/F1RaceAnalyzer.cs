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
	private double? _lastRainfall;
	private string? _lastFastestLapMessage;
	private readonly Dictionary<int, OpenF1Driver> _driverRegistry = [];
	private readonly Dictionary<int, bool> _overrideStates = [];
	private readonly Dictionary<int, int> _lastPitLaps = [];
	private readonly HashSet<int> _retiredDrivers = [];

	/// <summary>
	/// Resets the internal state of the object to its initial values.
	/// </summary>
	/// <remarks>Call this method to clear all cached data and restore the object to a clean state. This is
	/// typically used to prepare the object for reuse or to discard accumulated information from previous
	/// operations.</remarks>
	public void Reset() {
		_lastStatus = null;
		_lastP1Driver = null;
		_lastRainfall = null;
		_lastFastestLapMessage = null;

		_driverRegistry.Clear();
		_overrideStates.Clear();
		_lastPitLaps.Clear();
		_retiredDrivers.Clear();
	}

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
	/// Processes the provided weather data and generates a race event if there is a change in rainfall.
	/// </summary>
	/// <remarks>The method only generates a new event when the rainfall value differs from the previous value. If
	/// the 'rainfall' property is missing or unchanged, the method returns null.</remarks>
	/// <param name="weatherData">A nullable JSON element containing weather information. Must include a 'rainfall' property to be processed.</param>
	/// <returns>A RaceEvent instance representing the current weather conditions if rainfall has changed; otherwise, null.</returns>
	public RaceEvent? ProcessWeather(JsonElement? weatherData) {
		if (weatherData == null || weatherData.Value.ValueKind == JsonValueKind.Undefined) {
			return null;
		}

		if (weatherData.Value.TryGetProperty("rainfall", out var rainProp)) {
			double currentRainfall = rainProp.GetDouble();

			if (_lastRainfall != currentRainfall) {
				_lastRainfall = currentRainfall;
				bool isRaining = currentRainfall > 0;

				return new RaceEvent("f1/race/weather", JsonSerializer.Serialize(new {
					raining = isRaining,
					rainfall_value = currentRainfall
				}));
			}
		}

		return null;
	}

	/// <summary>
	/// Processes interval data for all registered drivers and generates events when the override availability status
	/// changes.
	/// </summary>
	/// <remarks>An event is generated only when a driver's override availability status transitions between
	/// available and unavailable states, based on the interval value. Override is considered available when the interval
	/// is greater than 0 and less than or equal to 1 second.</remarks>
	/// <param name="intervalsData">A list of JSON elements containing interval data for drivers. Each element should include a 'driver_number' and an
	/// 'interval' property. Can be null or empty.</param>
	/// <returns>A list of race events representing changes in override availability status for drivers. The list is empty if no
	/// changes are detected or if no interval data is provided.</returns>
	public List<RaceEvent> ProcessIntervals(List<JsonElement>? intervalsData) {
		var events = new List<RaceEvent>();

		if (intervalsData == null || intervalsData.Count == 0) {
			return events;
		}

		foreach (var driverNum in _driverRegistry.Keys) {
			// Search for the most recent entry for this specific driver
			var driverIntervalData = intervalsData.LastOrDefault(x =>
				x.TryGetProperty("driver_number", out var dNum) && dNum.GetInt32() == driverNum);

			if (driverIntervalData.ValueKind != JsonValueKind.Undefined &&
				driverIntervalData.TryGetProperty("interval", out var intervalProp)) {

				// some intervals come as null (e.g. P1 has no gap to the car in front)
				if (intervalProp.ValueKind == JsonValueKind.Null) {
					continue;
				}

				double currentInterval = intervalProp.GetDouble();

				// 2026 Manual Override Proxy: Available when gap is between 0 and 1 second (inclusive)
				bool canOverride = currentInterval > 0 && currentInterval <= 1.000;

				_overrideStates.TryGetValue(driverNum, out bool lastState);

				// Only fire an event if the override availability status has changed since the last check
				if (canOverride != lastState) {
					_overrideStates[driverNum] = canOverride;

					string shortName = _driverRegistry.TryGetValue(driverNum, out var d) ? d.NameAcronym : "UNK";
					string color = _driverRegistry.TryGetValue(driverNum, out var c) ? c.TeamColour : "FFFFFF";

					events.Add(new RaceEvent($"f1/driver/{driverNum}/events", JsonSerializer.Serialize(new {
						event_type = "OVERRIDE_AVAILABLE",
						driver_number = driverNum,
						short_name = shortName,
						color = color,
						value = canOverride,
						details = $"Gap: {currentInterval}s"
					})));

				}
			}
		}

		return events;
	}

	/// <summary>
	/// Processes pit stop data and generates race events for drivers who have made a new pit stop since the last check.
	/// </summary>
	/// <remarks>This method compares the provided pit stop data against previously processed laps to ensure that
	/// only new pit stop events are generated. It is intended to be called repeatedly as new pit stop data becomes
	/// available.</remarks>
	/// <param name="pitData">A list of JSON elements containing pit stop information for each driver. Each element should include the driver's
	/// number and the lap number of the pit stop. Can be null or empty if no pit stop data is available.</param>
	/// <returns>A list of race events representing new pit stop entries detected for drivers. The list is empty if no new pit stops
	/// are found.</returns>
	public List<RaceEvent> ProcessPitStops(List<JsonElement>? pitData) {
		var events = new List<RaceEvent>();

		if (pitData == null || pitData.Count == 0) {
			return events;
		}

		foreach (var driverNum in _driverRegistry.Keys) {
			var driverPitData = pitData.LastOrDefault(x =>
				x.TryGetProperty("driver_number", out var dNum) && dNum.GetInt32() == driverNum);

			if (driverPitData.ValueKind != JsonValueKind.Undefined &&
				driverPitData.TryGetProperty("lap", out var lapProp)) {

				int currentPitLap = lapProp.GetInt32();
				_lastPitLaps.TryGetValue(driverNum, out int lastProcessedLap);

				// We fire an event at the moment we see a new lap number associated with a pit stop for a driver.
				if (currentPitLap > lastProcessedLap) {
					_lastPitLaps[driverNum] = currentPitLap;

					string shortName = _driverRegistry.TryGetValue(driverNum, out var d) ? d.NameAcronym : "UNK";
					string color = _driverRegistry.TryGetValue(driverNum, out var c) ? c.TeamColour : "FFFFFF";

					events.Add(new RaceEvent($"f1/driver/{driverNum}/events", JsonSerializer.Serialize(new {
						event_type = "PIT_ENTRY",
						driver_number = driverNum,
						short_name = shortName,
						color = color,
						value = true,
						details = $"Lap: {currentPitLap}"
					})));
				}
			}
		}

		return events;
	}

	/// <summary>
	/// Processes race control messages to detect and record retirements for the specified drivers.
	/// </summary>
	/// <remarks>Each driver is only marked as retired once, even if multiple retirement messages are present. Only
	/// drivers not already marked as retired are processed.</remarks>
	/// <param name="raceControlData">A list of JSON elements representing race control messages to be analyzed for retirement events. Can be null or
	/// empty if no messages are available.</param>
	/// <returns>A list of RaceEvent objects representing detected retirements for the specified drivers. The list is empty if no
	/// retirements are found or if the input is null or empty.</returns>
	public List<RaceEvent> ProcessRetirements(List<JsonElement>? raceControlData) {
		var events = new List<RaceEvent>();

		if (raceControlData == null || raceControlData.Count == 0) {
			return events;
		}

		foreach (var driverNum in _driverRegistry.Keys) {
			// We only check drivers that are not already marked as retired
			if (!_retiredDrivers.Contains(driverNum)) {
				string searchString = $"CAR {driverNum}";

				var retirementMessage = raceControlData.LastOrDefault(x => {
					if (x.TryGetProperty("message", out var msgProp)) {
						string msg = msgProp.GetString()?.ToUpperInvariant() ?? "";
						return msg.Contains(searchString) && (msg.Contains("RETIRED") || msg.Contains("STOPPED"));
					}
					return false;
				});

				if (retirementMessage.ValueKind != JsonValueKind.Undefined) {
					_retiredDrivers.Add(driverNum); // Driver is out - we won't process any further messages for this driver

					string shortName = _driverRegistry.TryGetValue(driverNum, out var d) ? d.NameAcronym : "UNK";
					string color = _driverRegistry.TryGetValue(driverNum, out var c) ? c.TeamColour : "FFFFFF";
					string exactMessage = retirementMessage.GetProperty("message").GetString() ?? "Retired";

					events.Add(new RaceEvent($"f1/driver/{driverNum}/events", JsonSerializer.Serialize(new {
						event_type = "RETIRED",
						driver_number = driverNum,
						short_name = shortName,
						color = color,
						value = true,
						details = exactMessage
					})));
				}
			}
		}

		return events;
	}

	/// <summary>
	/// Processes the provided race control data to detect and generate events for the latest fastest lap message.
	/// </summary>
	/// <remarks>Only the most recent fastest lap message is considered, and duplicate messages are ignored. Events
	/// are generated only for drivers present in the registry.</remarks>
	/// <param name="raceControlData">A list of JSON elements representing race control messages to be analyzed for fastest lap events. Can be null or
	/// empty.</param>
	/// <returns>A list of race events corresponding to new fastest lap messages detected in the input data. The list is empty if no
	/// new fastest lap is found.</returns>
	public List<RaceEvent> ProcessFastestLap(List<JsonElement>? raceControlData) {
		var events = new List<RaceEvent>();

		if (raceControlData == null || raceControlData.Count == 0) {
			return events;
		}

		// We look for the absolute last message
		var fastestLapMsgNode = raceControlData.LastOrDefault(x =>
			x.TryGetProperty("message", out var msgProp) &&
			(msgProp.GetString()?.ToUpperInvariant().Contains("FASTEST LAP") ?? false)
		);

		if (fastestLapMsgNode.ValueKind != JsonValueKind.Undefined) {
			string msg = fastestLapMsgNode.GetProperty("message").GetString()!.ToUpperInvariant();

			// Only process if this is a NEW fastest lap message we haven't seen before, to avoid duplicates
			if (msg != _lastFastestLapMessage) {
				_lastFastestLapMessage = msg;

				// check if the message contains any of our drivers - if yes, this is the new fastest lap holder and we fire an event for that driver
				foreach (var driverNum in _driverRegistry.Keys) {
					if (msg.Contains($"CAR {driverNum}")) {
						string shortName = _driverRegistry.TryGetValue(driverNum, out var d) ? d.NameAcronym : "UNK";
						string color = _driverRegistry.TryGetValue(driverNum, out var c) ? c.TeamColour : "FFFFFF";

						events.Add(new RaceEvent($"f1/driver/{driverNum}/events", JsonSerializer.Serialize(new {
							event_type = "FASTEST_LAP",
							driver_number = driverNum,
							short_name = shortName,
							color = color,
							value = true,
							details = msg
						})));
					}
				}
			}
		}

		return events;
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