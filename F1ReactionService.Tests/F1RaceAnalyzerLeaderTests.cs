using F1ReactionService.Model;
using FluentAssertions;
using System.Text.Json;

namespace F1ReactionService.Tests;

/// <summary>
/// Contains unit tests for the F1RaceAnalyzer class that verify the behavior of leader processing in Formula 1 race
/// scenarios.
/// </summary>
/// <remarks>These tests cover scenarios such as detecting a change in the race leader, handling unchanged leader
/// states to prevent redundant notifications, and managing cases where the leader is not found in the driver registry.
/// The tests use sample driver data and simulated API responses to validate the expected outcomes of the ProcessLeader
/// method.</remarks>
public class F1RaceAnalyzerLeaderTests {

	/// <summary>
	/// Verifies that the ProcessLeader method returns a race event when the leader changes to a known driver.
	/// </summary>
	/// <remarks>This test ensures that when a known driver becomes the leader during a race, the resulting event
	/// contains the correct driver and team information in its payload. It also checks that the event topic is set
	/// appropriately for a race leader scenario.</remarks>
	[Fact]
	public void ProcessLeader_ShouldReturnRaceEvent_WhenP1ChangesToKnownDriver() {
		var analyzer = new F1RaceAnalyzer();

		var max = new OpenF1Driver {
			DriverNumber = 1,
			FullName = "Max Verstappen",
			NameAcronym = "VER",
			TeamName = "Red Bull Racing",
			TeamColour = "3671C6"
		};
		analyzer.UpdateDriverRegistry([max]);

		var p1Json = ParseJson("{\"driver_number\": 1}");

		var result = analyzer.ProcessLeader(p1Json, isRace: true, currentSessionName: "Race", isLive: true);

		result.Should().NotBeNull();
		result!.Topic.Should().Be("f1/race/p1");

		result.Payload.Should().Contain("Max Verstappen");
		result.Payload.Should().Contain("VER");
		result.Payload.Should().Contain("Red Bull Racing");
		result.Payload.Should().Contain("Race Leader");
	}

	/// <summary>
	/// Verifies that the ProcessLeader method returns null when the leader's driver number remains unchanged between
	/// consecutive calls.
	/// </summary>
	/// <remarks>This test ensures that ProcessLeader does not produce duplicate or unnecessary results when the
	/// leader has not changed, which helps prevent redundant notifications or processing.</remarks>
	[Fact]
	public void ProcessLeader_ShouldReturnNull_WhenP1RemainsUnchanged() {
		var analyzer = new F1RaceAnalyzer();
		var max = new OpenF1Driver { DriverNumber = 1, FullName = "Max Verstappen" };
		analyzer.UpdateDriverRegistry([max]);
		var p1Json = ParseJson("{\"driver_number\": 1}");

		analyzer.ProcessLeader(p1Json, true, "Race", true);

		var result = analyzer.ProcessLeader(p1Json, true, "Race", true);

		result.Should().BeNull("the drivers number did not change and we do not want to spam systems");
	}

	/// <summary>
	/// Verifies that the ProcessLeader method returns null when the specified driver is not present in the driver
	/// registry.
	/// </summary>
	/// <remarks>This test ensures that ProcessLeader does not return a result for a driver number that has not been
	/// registered, confirming correct handling of unknown drivers.</remarks>
	[Fact]
	public void ProcessLeader_ShouldReturnNull_WhenDriverIsNotInRegistry() {
		var analyzer = new F1RaceAnalyzer();

		var p1Json = ParseJson("{\"driver_number\": 99}");

		var result = analyzer.ProcessLeader(p1Json, true, "Race", true);

		result.Should().BeNull("the driver with the number 99 was not found in the local dictionary");
	}

	/// <summary>
	/// Parses the specified JSON string and returns its root element as a JsonElement.
	/// </summary>
	/// <remarks>The returned JsonElement is valid only as long as the underlying data is not disposed. If the input
	/// string is not valid JSON, a JsonException is thrown.</remarks>
	/// <param name="jsonString">The JSON string to parse. Must be a valid JSON document.</param>
	/// <returns>A JsonElement representing the root element of the parsed JSON document.</returns>
	private static JsonElement ParseJson(string jsonString) {
		return JsonSerializer.Deserialize<JsonElement>(jsonString);
	}
}