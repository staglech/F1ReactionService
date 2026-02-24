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
}