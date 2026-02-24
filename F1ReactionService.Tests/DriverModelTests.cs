using F1ReactionService.Model;
using FluentAssertions;

namespace F1ReactionService.Tests;

/// <summary>
/// Contains unit tests for verifying the behavior of the OpenF1Driver model, specifically related to the TeamColour
/// property.
/// </summary>
/// <remarks>These tests ensure that the TeamColour property correctly handles hex color codes, including
/// appending a hash symbol when necessary and providing a default value when no color is specified.</remarks>
public class DriverModelTests {

	/// <summary>
	/// Verifies that the TeamColour property automatically prepends a hash character ('#') when a hexadecimal color code
	/// is assigned without one.
	/// </summary>
	/// <remarks>This test ensures that assigning a hex color code without a leading hash to the TeamColour property
	/// results in the value being stored with the hash, matching expected formatting conventions.</remarks>
	[Fact]
	public void TeamColour_ShouldAppendHash_WhenHexCodeIsProvidedWithoutHash() {
		var driver = new OpenF1Driver {
			TeamColour = "ED1131"
		};

		driver.TeamColour.Should().Be("#ED1131");
	}

	/// <summary>
	/// Verifies that setting the TeamColour property with a value that already includes a leading hash character does not
	/// result in an additional hash being appended.
	/// </summary>
	/// <remarks>This test ensures that the TeamColour property preserves the format of color values that already
	/// begin with a hash ('#'), preventing unintended formatting changes.</remarks>
	[Fact]
	public void TeamColour_ShouldNotAppendSecondHash_WhenHashIsAlreadyPresent() {
		var driver = new OpenF1Driver {
			TeamColour = "#27F4D2"
		};

		driver.TeamColour.Should().Be("#27F4D2");
	}

	/// <summary>
	/// Verifies that the TeamColour property returns the default white color when the API provides no color value.
	/// </summary>
	/// <remarks>This test ensures that the TeamColour property uses a safe fallback value of "#FFFFFF" when the API
	/// does not supply a valid color. This behavior helps prevent errors or unexpected UI results when color data is
	/// missing.</remarks>
	/// <param name="emptyValue">A string representing an empty or null color value returned by the API. Can be null, empty, or whitespace.</param>
	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData(null)]
	public void TeamColour_ShouldReturnWhite_WhenApiReturnsNoColor(string emptyValue) {
		var driver = new OpenF1Driver {
			TeamColour = emptyValue
		};

		driver.TeamColour.Should().Be("#FFFFFF");
	}
}