using F1ReactionService.Model;
using FluentAssertions;

namespace F1ReactionService.Tests;

public class DriverModelTests {
	[Fact] // [Fact] nutzt man, wenn man keine Inline-Daten übergibt
	public void TeamColour_ShouldAppendHash_WhenHexCodeIsProvidedWithoutHash() {
		// Arrange (Vorbereitung)
		var driver = new OpenF1Driver {
			// Act (Aktion)
			// Die API liefert z.B. Ferrari-Rot ohne Raute
			TeamColour = "ED1131"
		};

		// Assert (Prüfung)
		driver.TeamColour.Should().Be("#ED1131");
	}

	[Fact]
	public void TeamColour_ShouldNotAppendSecondHash_WhenHashIsAlreadyPresent() {
		// Arrange
		var driver = new OpenF1Driver {
			// Act
			TeamColour = "#27F4D2" // Mercedes-Farbe, hat schon eine Raute
		};

		// Assert
		driver.TeamColour.Should().Be("#27F4D2");
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData(null)]
	public void TeamColour_ShouldReturnWhite_WhenApiReturnsNoColor(string emptyValue) {
		// Arrange
		var driver = new OpenF1Driver {
			// Act
			TeamColour = emptyValue
		};

		// Assert
		driver.TeamColour.Should().Be("#FFFFFF"); // Unser sicherer Fallback
	}
}