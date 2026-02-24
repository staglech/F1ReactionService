using FluentAssertions;

namespace F1ReactionService.Tests;

/// <summary>
/// Contains unit tests for verifying the mapping of status codes to flag names in the F1RaceAnalyzer class.
/// </summary>
/// <remarks>These tests ensure that known status codes are correctly mapped to their corresponding flag names and
/// that invalid or unknown codes return "UNKNOWN". The tests use parameterized inputs to cover multiple scenarios
/// efficiently.</remarks>
public class FlagMappingTests {

	/// <summary>
	/// Verifies that the MapFlag method returns the correct flag name for known status codes.
	/// </summary>
	/// <remarks>This test uses multiple input values to ensure that the MapFlag method correctly maps each known
	/// status code to its corresponding flag name. It helps validate the mapping logic for all supported codes.</remarks>
	/// <param name="statusCode">The status code to map to a flag name. Must be a string representing a known flag status code.</param>
	/// <param name="expectedFlag">The expected flag name corresponding to the provided status code.</param>
	[Theory]
	[InlineData("1", "GREEN")]
	[InlineData("2", "YELLOW")]
	[InlineData("4", "SC")]
	[InlineData("5", "RED")]
	[InlineData("6", "VSC")]
	public void MapFlag_ShouldReturnCorrectFlagName_WhenGivenKnownStatusCode(string statusCode, string expectedFlag) {
		var result = F1RaceAnalyzer.MapFlag(statusCode);
		result.Should().Be(expectedFlag);
	}

	/// <summary>
	/// Verifies that the MapFlag method returns "UNKNOWN" when provided with an invalid status code.
	/// </summary>
	/// <remarks>This test ensures that invalid or unrecognized status codes are handled gracefully by returning the
	/// default "UNKNOWN" flag value.</remarks>
	/// <param name="invalidCode">A status code that does not correspond to a known flag. Can be null, empty, or an unrecognized value.</param>
	[Theory]
	[InlineData("99")]
	[InlineData("irgendwas")]
	[InlineData("")]
	[InlineData(null)]
	public void MapFlag_ShouldReturnUnknown_WhenGivenInvalidStatusCode(string invalidCode) {
		var result = F1RaceAnalyzer.MapFlag(invalidCode);
		result.Should().Be("UNKNOWN");
	}
}