using FluentAssertions;
using System.Text.Json;

namespace F1ReactionService.Tests;

public class F1RaceAnalyzerTests {
	[Fact]
	public void ProcessTrackStatus_ShouldReturnRaceEvent_WhenStatusChangesToRed() {
		// Arrange
		var analyzer = new F1RaceAnalyzer();

		// Wir faken einfach den JsonElement-Input, den das OpenF1-JSON liefern würde
		string jsonString = "{\"status\": \"5\", \"message\": \"Session Suspended\"}";
		var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonString);

		// Act
		var result = analyzer.ProcessTrackStatus(jsonElement);

		// Assert
		result.Should().NotBeNull();
		result!.Topic.Should().Be("f1/race/flag_status");
		result.Payload.Should().Contain("\"flag\":\"RED\"");
	}

	[Fact]
	public void ProcessTrackStatus_ShouldReturnNull_WhenStatusRemainsTheSame() {
		// Arrange
		var analyzer = new F1RaceAnalyzer();
		string jsonString = "{\"status\": \"1\", \"message\": \"Track Clear\"}";
		var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonString);

		// Act 1: Erster Aufruf (Wechsel von "Unknown" auf Grün)
		analyzer.ProcessTrackStatus(jsonElement);

		// Act 2: Zweiter Aufruf (Es ist immer noch Grün)
		var result = analyzer.ProcessTrackStatus(jsonElement);

		// Assert
		result.Should().BeNull("weil sich der Status beim zweiten Durchlauf nicht geändert hat");
	}
}