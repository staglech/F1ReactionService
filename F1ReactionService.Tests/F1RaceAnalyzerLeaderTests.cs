using F1ReactionService.Model;
using FluentAssertions;
using System.Text.Json;

namespace F1ReactionService.Tests;

public class F1RaceAnalyzerLeaderTests {
	// Kleine Hilfsmethode, damit wir nicht in jedem Test das JSON-Element tippen müssen
	private static JsonElement ParseJson(string jsonString) {
		return JsonSerializer.Deserialize<JsonElement>(jsonString);
	}

	[Fact]
	public void ProcessLeader_ShouldReturnRaceEvent_WhenP1ChangesToKnownDriver() {
		// Arrange
		var analyzer = new F1RaceAnalyzer();

		// 1. Dem Analyzer einen Fahrer "beibringen"
		var max = new OpenF1Driver {
			DriverNumber = 1,
			FullName = "Max Verstappen",
			NameAcronym = "VER",
			TeamName = "Red Bull Racing",
			TeamColour = "3671C6"
		};
		analyzer.UpdateDriverRegistry(new[] { max });

		// 2. Das JSON faken, das von der API kommen würde
		var p1Json = ParseJson("{\"driver_number\": 1}");

		// Act
		var result = analyzer.ProcessLeader(p1Json, isRace: true, currentSessionName: "Race", isLive: true);

		// Assert
		result.Should().NotBeNull();
		result!.Topic.Should().Be("f1/race/p1");

		// Prüfen, ob die Daten sauber im Payload gelandet sind
		result.Payload.Should().Contain("Max Verstappen");
		result.Payload.Should().Contain("VER");
		result.Payload.Should().Contain("Red Bull Racing");
		result.Payload.Should().Contain("Race Leader"); // Weil isRace = true war
	}

	[Fact]
	public void ProcessLeader_ShouldReturnNull_WhenP1RemainsUnchanged() {
		// Arrange
		var analyzer = new F1RaceAnalyzer();
		var max = new OpenF1Driver { DriverNumber = 1, FullName = "Max Verstappen" };
		analyzer.UpdateDriverRegistry(new[] { max });
		var p1Json = ParseJson("{\"driver_number\": 1}");

		// Act 1: Der erste Aufruf (Wechsel auf Max)
		analyzer.ProcessLeader(p1Json, true, "Race", true);

		// Act 2: Eine Sekunde später ruft der Worker wieder an. Max ist immer noch P1.
		var result = analyzer.ProcessLeader(p1Json, true, "Race", true);

		// Assert
		result.Should().BeNull("weil sich die Startnummer nicht geändert hat und wir keinen Spam wollen");
	}

	[Fact]
	public void ProcessLeader_ShouldReturnNull_WhenDriverIsNotInRegistry() {
		// Arrange
		var analyzer = new F1RaceAnalyzer();

		// ACHTUNG: Wir rufen UpdateDriverRegistry absichtlich NICHT auf. Die Liste ist leer.

		// Die API behauptet plötzlich, Startnummer 99 ist auf P1
		var p1Json = ParseJson("{\"driver_number\": 99}");

		// Act
		var result = analyzer.ProcessLeader(p1Json, true, "Race", true);

		// Assert
		result.Should().BeNull("weil der Fahrer mit der Nummer 99 nicht im lokalen Wörterbuch gefunden wurde");
	}
}