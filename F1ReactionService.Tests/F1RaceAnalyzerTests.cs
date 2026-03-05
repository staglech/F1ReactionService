using F1ReactionService.Model;
using FluentAssertions;
using System.Text.Json;

namespace F1ReactionService.Tests;

/// <summary>
/// Contains unit tests for the F1RaceAnalyzer class, verifying its behavior when processing track status updates in a
/// Formula 1 race context.
/// </summary>
/// <remarks>These tests ensure that F1RaceAnalyzer correctly identifies changes in track status, such as
/// transitions to a red flag, and handles repeated status inputs appropriately. The tests use sample JSON data to
/// simulate inputs from an external source and validate the analyzer's output.</remarks>
public class F1RaceAnalyzerTests {

	private readonly F1RaceAnalyzer _analyzer;
	private readonly List<int> _trackedDrivers;

	public F1RaceAnalyzerTests() {
		_analyzer = new F1RaceAnalyzer();

		// Wir legen Charles Leclerc (16) für unsere Tests als Basis in die Registry
		_analyzer.UpdateDriverRegistry([
			new OpenF1Driver { DriverNumber = 16, FullName = "Charles Leclerc", NameAcronym = "LEC", TeamColour = "FF0000" }
		]);

		_trackedDrivers = [16];
	}

	/// <summary>
	/// Verifies that the ProcessTrackStatus method returns a race event with a red flag status when the track status
	/// changes to suspended.
	/// </summary>
	/// <remarks>This test simulates a scenario where the input JSON indicates a session suspension, corresponding
	/// to a red flag in Formula 1. It asserts that the returned event contains the correct topic and payload reflecting
	/// the red flag status.</remarks>
	[Fact]
	public void ProcessTrackStatus_ShouldReturnRaceEvent_WhenStatusChangesToRed() {
		var analyzer = new F1RaceAnalyzer();

		string jsonString = "{\"status\": \"5\", \"message\": \"Session Suspended\"}";
		var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonString);
		var result = analyzer.ProcessTrackStatus(jsonElement);

		result.Should().NotBeNull();
		result!.Topic.Should().Be("f1/race/flag_status");
		result.Payload.Should().Contain("\"flag\":\"RED\"");
	}

	/// <summary>
	/// Verifies that the ProcessTrackStatus method returns null when the track status does not change between consecutive
	/// calls.
	/// </summary>
	/// <remarks>This test ensures that no result is produced if the status remains the same, confirming that the
	/// method only returns a value when a status change occurs.</remarks>
	[Fact]
	public void ProcessTrackStatus_ShouldReturnNull_WhenStatusRemainsTheSame() {
		var analyzer = new F1RaceAnalyzer();
		string jsonString = "{\"status\": \"1\", \"message\": \"Track Clear\"}";
		var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonString);

		analyzer.ProcessTrackStatus(jsonElement);
		var result = analyzer.ProcessTrackStatus(jsonElement);

		result.Should().BeNull("the status in the second run has not chaned");
	}

	/// <summary>
	/// Verifies that the override available event is fired when the interval gap is less than one second.
	/// </summary>
	/// <remarks>This test ensures that when a driver is within 0.8 seconds of the car ahead, the analyzer correctly
	/// emits an event indicating that an overtake opportunity is available. The event payload should include the topic for
	/// the driver and set the value to true.</remarks>
	[Fact]
	public void ProcessIntervals_GapUnderOneSecond_ShouldFireOverrideAvailableEvent() {
		// Arrange: Leclerc is 0.8 seconds behind someone
		string json = @"[{""driver_number"": 16, ""interval"": 0.800}]";
		var mockData = JsonSerializer.Deserialize<List<JsonElement>>(json);

		var events = _analyzer.ProcessIntervals(mockData);

		events.Should().ContainSingle();
		events[0].Topic.Should().Be("f1/driver/16/events");
		events[0].Payload.Should().Contain("OVERRIDE_AVAILABLE");
		events[0].Payload.Should().Contain("\"value\":true");
	}

	/// <summary>
	/// Verifies that the analyzer fires an override disabled event when a gap over one second is detected after previously
	/// being enabled.
	/// </summary>
	/// <remarks>This test simulates interval data where the gap transitions from under one second to over one
	/// second for a tracked driver. It asserts that the resulting event payload correctly indicates the override has been
	/// disabled.</remarks>
	[Fact]
	public void ProcessIntervals_GapOverOneSecond_ShouldFireOverrideDisabledEvent_IfPreviouslyEnabled() {
		var dataUnder = JsonSerializer.Deserialize<List<JsonElement>>(@"[{""driver_number"": 16, ""interval"": 0.800}]");
		var dataOver = JsonSerializer.Deserialize<List<JsonElement>>(@"[{""driver_number"": 16, ""interval"": 1.500}]");

		_analyzer.ProcessIntervals(dataUnder);
		var events = _analyzer.ProcessIntervals(dataOver);

		events.Should().ContainSingle();
		events[0].Payload.Should().Contain("\"value\":false");
	}

	/// <summary>
	/// Verifies that the pit entry event is fired only once per lap when processing pit stops for a driver.
	/// </summary>
	/// <remarks>This test ensures that repeated calls to the pit stop processing method with the same lap data do
	/// not result in duplicate pit entry events for the same driver and lap. It helps prevent event flooding and maintains
	/// correct event semantics.</remarks>
	[Fact]
	public void ProcessPitStops_NewLap_ShouldFirePitEntryEvent_OnlyOncePerLap() {
		// Arrange: Leclerc drives to box in round 14
		var pitData = JsonSerializer.Deserialize<List<JsonElement>>(@"[{""driver_number"": 16, ""lap"": 14}]");

		var firstCheck = _analyzer.ProcessPitStops(pitData);
		var secondCheck = _analyzer.ProcessPitStops(pitData);

		firstCheck.Should().ContainSingle();
		firstCheck[0].Topic.Should().Be("f1/driver/16/events");
		firstCheck[0].Payload.Should().Contain("PIT_ENTRY");

		secondCheck.Should().BeEmpty("The event should only fire once per pit stop lap.");
	}

	/// <summary>
	/// Verifies that processing a retirement message fires the appropriate event and records the driver's retired status
	/// to prevent duplicate notifications.
	/// </summary>
	/// <remarks>This test ensures that when a retirement message is received for a driver, the analyzer emits the
	/// correct event only once, even if the same message is processed multiple times. This prevents redundant
	/// notifications for already retired drivers.</remarks>
	[Fact]
	public void ProcessRetirements_RetiredMessage_ShouldFireEventAndRememberStatus() {
		// Arrange: official message from race control
		var rcData = JsonSerializer.Deserialize<List<JsonElement>>(
			@"[{""message"": ""CAR 16 (LEC) RETIRED DUE TO HYDRAULICS""}]"
		);

		var firstCheck = _analyzer.ProcessRetirements(rcData);
		var secondCheck = _analyzer.ProcessRetirements(rcData);

		firstCheck.Should().ContainSingle();
		firstCheck[0].Topic.Should().Be("f1/driver/16/events");
		firstCheck[0].Payload.Should().Contain("RETIRED");
		firstCheck[0].Payload.Should().Contain("HYDRAULICS");

		secondCheck.Should().BeEmpty("The driver is already marked as retired, no spam allowed.");
	}

	/// <summary>
	/// Verifies that processing a new fastest lap message triggers the FastestLap event and that duplicate messages do not
	/// trigger additional events.
	/// </summary>
	/// <remarks>This test ensures that the analyzer correctly identifies and emits an event only when a new fastest
	/// lap message is received. It also checks that repeated processing of the same message does not result in duplicate
	/// events, and that subsequent new fastest lap messages are handled appropriately.</remarks>
	[Fact]
	public void ProcessFastestLap_NewMessage_ShouldFireFastestLapEvent() {
		var rcData1 = JsonSerializer.Deserialize<List<JsonElement>>(@"[{""message"": ""FASTEST LAP - CAR 16 (LEC) - 1:24.321""}]");
		var rcData2 = JsonSerializer.Deserialize<List<JsonElement>>(@"[{""message"": ""FASTEST LAP - CAR 16 (LEC) - 1:23.999""}]");

		var events1 = _analyzer.ProcessFastestLap(rcData1);
		var emptyCheck = _analyzer.ProcessFastestLap(rcData1);
		var events2 = _analyzer.ProcessFastestLap(rcData2);

		events1.Should().ContainSingle();
		events1[0].Topic.Should().Be("f1/driver/16/events");
		events1[0].Payload.Should().Contain("FASTEST_LAP");

		emptyCheck.Should().BeEmpty();

		events2.Should().ContainSingle("A new fastest lap message should trigger a new event.");
	}

	/// <summary>
	/// Verifies that calling the Reset method clears all internal states of the analyzer, allowing previously processed
	/// retirements to be detected again.
	/// </summary>
	/// <remarks>This test ensures that both the retired drivers list and the driver registry are reset, so that
	/// after re-registering a driver, retirement events are processed as new. Use this test to confirm that the analyzer's
	/// Reset method fully restores its initial state for subsequent operations.</remarks>
	[Fact]
	public void Reset_ShouldClearAllInternalStates() {
		var rcData = JsonSerializer.Deserialize<List<JsonElement>>(@"[{""message"": ""CAR 16 (LEC) STOPPED ON TRACK""}]");

		var mockDriver = new OpenF1Driver { DriverNumber = 16, NameAcronym = "LEC", TeamColour = "FF0000" };
		_analyzer.UpdateDriverRegistry([mockDriver]);
		_analyzer.ProcessRetirements(rcData);
		_analyzer.Reset();
		_analyzer.UpdateDriverRegistry([mockDriver]);

		var eventsAfterReset = _analyzer.ProcessRetirements(rcData);

		eventsAfterReset.Should().ContainSingle("Because the internal state was reset, the retirement should be processed as new.");
	}
}