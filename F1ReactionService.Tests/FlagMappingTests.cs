using FluentAssertions;

namespace F1ReactionService.Tests;

public class FlagMappingTests {
	[Theory]
	[InlineData("1", "GREEN")]
	[InlineData("2", "YELLOW")]
	[InlineData("4", "SC")]
	[InlineData("5", "RED")]
	[InlineData("6", "VSC")]
	public void MapFlag_ShouldReturnCorrectFlagName_WhenGivenKnownStatusCode(string statusCode, string expectedFlag) {
		// Act (Wir rufen die Methode auf)
		var result = F1RaceAnalyzer.MapFlag(statusCode);

		// Assert (Wir prüfen das Ergebnis mit FluentAssertions)
		// Liest sich wie ein englischer Satz: "result should be expectedFlag"
		result.Should().Be(expectedFlag);
	}

	[Theory]
	[InlineData("99")]
	[InlineData("irgendwas")]
	[InlineData("")]
	[InlineData(null)]
	public void MapFlag_ShouldReturnUnknown_WhenGivenInvalidStatusCode(string invalidCode) {
		// Act
		var result = F1RaceAnalyzer.MapFlag(invalidCode);

		// Assert
		result.Should().Be("UNKNOWN");
	}
}