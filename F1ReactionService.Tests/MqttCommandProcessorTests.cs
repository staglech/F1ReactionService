using F1ReactionService.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace F1ReactionService.Tests;

/// <summary>
/// Contains unit tests for the MqttCommandProcessor class, verifying correct handling of various MQTT command scenarios
/// and their effects on session state.
/// </summary>
/// <remarks>These tests cover command processing for session activation, demo mode, calibration, driver tracking,
/// and error handling. The tests ensure that the MqttCommandProcessor responds appropriately to valid and invalid
/// commands, updating the F1SessionState as expected. This class uses xUnit for test methods and NSubstitute for
/// mocking dependencies.</remarks>
public class MqttCommandProcessorTests {
	private readonly ILogger<MqttCommandProcessor> _loggerMock;
	private readonly F1SessionState _sessionState;
	private readonly MqttCommandProcessor _processor;

	/// <summary>
	/// Initializes a new instance of the MqttCommandProcessorTests class for unit testing the MqttCommandProcessor.
	/// </summary>
	/// <remarks>This constructor sets up the required test dependencies, including a mock logger and a session
	/// state, to facilitate isolated and repeatable tests of the MqttCommandProcessor.</remarks>
	public MqttCommandProcessorTests() {
		_loggerMock = Substitute.For<ILogger<MqttCommandProcessor>>();
		_sessionState = new F1SessionState();
		_processor = new MqttCommandProcessor(_loggerMock, _sessionState);
	}

	/// <summary>
	/// Verifies that processing the "START" command activates the session and releases the wake-up signal as expected.
	/// </summary>
	/// <remarks>This test ensures that when the "START" command is processed, the session state becomes active,
	/// demo mode is disabled, and the wake-up signal is incremented. Use this test to validate correct session activation
	/// behavior in response to the command.</remarks>
	[Fact]
	public void ProcessCommand_START_ShouldActivateSessionAndReleaseSignal() {
		_processor.ProcessCommand("START");

		_sessionState.IsActive.Should().BeTrue();
		_sessionState.IsDemoMode.Should().BeFalse();
		_sessionState.WakeUpSignal.CurrentCount.Should().BeGreaterThan(0);
	}

	/// <summary>
	/// Verifies that processing the "DEMO_START" command activates demo mode and updates the session state accordingly.
	/// </summary>
	/// <remarks>This test ensures that when the "DEMO_START" command is processed, the session becomes active, demo
	/// mode is enabled, and the wake-up signal is incremented. Use this test to validate correct behavior when initiating
	/// demo mode through command processing.</remarks>
	[Fact]
	public void ProcessCommand_DEMO_START_ShouldActivateDemoMode() {
		_processor.ProcessCommand("DEMO_START");

		_sessionState.IsActive.Should().BeTrue();
		_sessionState.IsDemoMode.Should().BeTrue();
		_sessionState.WakeUpSignal.CurrentCount.Should().BeGreaterThan(0);
	}

	/// <summary>
	/// Verifies that processing the "STOP" command deactivates the session and clears the session start time.
	/// </summary>
	/// <remarks>This test ensures that when the "STOP" command is processed, the session state is set to inactive,
	/// demo mode is disabled, and the session's start time is cleared. Use this test to validate correct session
	/// termination behavior in the command processor.</remarks>
	[Fact]
	public void ProcessCommand_STOP_ShouldDeactivateSessionAndClearStartTime() {
		_sessionState.IsActive = true;
		_sessionState.TrueDataStartTime = DateTime.UtcNow;

		_processor.ProcessCommand("STOP");

		_sessionState.IsActive.Should().BeFalse();
		_sessionState.IsDemoMode.Should().BeFalse();
		_sessionState.TrueDataStartTime.Should().BeNull();
	}

	/// <summary>
	/// Verifies that processing the "CALIBRATE_START" command correctly calculates and updates the current delay based on
	/// the elapsed time since the true data start time.
	/// </summary>
	/// <remarks>This test ensures that the delay calculation logic in the command processor accurately reflects the
	/// time difference between the current time and the session's true data start time. The test asserts that the
	/// resulting delay is within an expected range, validating the calibration behavior.</remarks>
	[Fact]
	public void ProcessCommand_CALIBRATE_START_ShouldCalculateCurrentDelay() {
		var startTime = DateTime.UtcNow.AddSeconds(-30);
		_sessionState.TrueDataStartTime = startTime;
		_sessionState.CurrentDelay = TimeSpan.Zero;

		_processor.ProcessCommand("CALIBRATE_START");

		_sessionState.CurrentDelay.Should().BeGreaterThan(TimeSpan.FromSeconds(29));
		_sessionState.CurrentDelay.Should().BeLessThan(TimeSpan.FromSeconds(31));
	}

	/// <summary>
	/// Verifies that the ProcessCommand method correctly adds a driver to the TrackedDrivers dictionary when the TRACK_ADD
	/// command is processed.
	/// </summary>
	/// <remarks>This test ensures that invoking ProcessCommand with a TRACK_ADD command results in the specified
	/// driver being present in the TrackedDrivers dictionary with a value of true.</remarks>
	[Fact]
	public void ProcessCommand_TRACK_ADD_ShouldAddDriverToDictionary() {
		_processor.ProcessCommand("TRACK_ADD_16");

		_sessionState.TrackedDrivers.Should().ContainKey(16);
		_sessionState.TrackedDrivers[16].Should().BeTrue();
	}

	/// <summary>
	/// Verifies that the ProcessCommand method removes the specified driver from the TrackedDrivers dictionary when a
	/// TRACK_REMOVE command is processed.
	/// </summary>
	/// <remarks>This test ensures that only the driver with the specified ID is removed and that other tracked
	/// drivers remain unaffected.</remarks>
	[Fact]
	public void ProcessCommand_TRACK_REMOVE_ShouldRemoveDriverFromDictionary() {
		_sessionState.TrackedDrivers.TryAdd(44, true);
		_sessionState.TrackedDrivers.TryAdd(16, true);

		_processor.ProcessCommand("TRACK_REMOVE_44");

		_sessionState.TrackedDrivers.Should().NotContainKey(44);
		_sessionState.TrackedDrivers.Should().ContainKey(16);
	}

	/// <summary>
	/// Verifies that invoking the "TRACK_CLEAR" command results in the tracked drivers dictionary being emptied.
	/// </summary>
	/// <remarks>This unit test ensures that after processing the "TRACK_CLEAR" command, all entries are removed
	/// from the tracked drivers collection. Use this test to confirm correct command handling and state reset
	/// behavior.</remarks>
	[Fact]
	public void ProcessCommand_TRACK_CLEAR_ShouldEmptyDictionary() {
		_sessionState.TrackedDrivers.TryAdd(1, true);
		_sessionState.TrackedDrivers.TryAdd(33, true);

		_processor.ProcessCommand("TRACK_CLEAR");

		_sessionState.TrackedDrivers.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that processing an invalid or unknown command does not throw an exception and does not alter the session
	/// state.
	/// </summary>
	/// <remarks>This test ensures that the session remains unchanged and no exceptions are thrown when an invalid
	/// or unrecognized command is processed. It helps validate the robustness of the command processor against unexpected
	/// input.</remarks>
	/// <param name="invalidCommand">The command string to process. Can be an unknown, invalid, empty, or whitespace-only command.</param>
	[Theory]
	[InlineData("UNKNOWN_COMMAND")]
	[InlineData("TRACK_ADD_INVALID")]
	[InlineData("")]
	[InlineData("   ")]
	public void ProcessCommand_InvalidOrUnknownCommand_ShouldNotThrowAndNotChangeState(string invalidCommand) {
		var initialState = _sessionState.IsActive;

		Action act = () => _processor.ProcessCommand(invalidCommand);

		act.Should().NotThrow();
		_sessionState.IsActive.Should().Be(initialState);
		_sessionState.TrackedDrivers.Should().BeEmpty();
	}
}