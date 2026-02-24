using F1ReactionService.Model;
using System.Text.Json;

namespace F1ReactionService;

/// <summary>
/// Provides a custom HTTP message handler that simulates responses for a demo Formula 1 session, including session,
/// driver, track status, and position data.
/// </summary>
/// <remarks>This handler is intended for use in demo or testing scenarios where live data is not available. It
/// generates predictable, scripted responses to specific API endpoints, allowing client code to operate as if it were
/// communicating with a real backend. The handler automatically manages the demo session state and can end the demo
/// after a set number of simulated intervals. Thread safety is not guaranteed; use in single-threaded or controlled
/// environments.</remarks>
public class DemoHttpMessageHandler(F1SessionState sessionState) : HttpMessageHandler {
	private int _tickCount = 0;
	private readonly F1SessionState _sessionState = sessionState;

	/// <inheritdoc/>
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
		var path = request.RequestUri?.ToString() ?? string.Empty;
		string content = "[]";

		if (path.Contains("sessions")) {
			// we fake a sunning session in order to prevent the worker switch to standby
			var demoSession = new[] {
				new { session_name = "Demo Race", date_start = "2020-01-01T00:00:00Z", date_end = "2099-01-01T00:00:00Z" }
			};
			content = JsonSerializer.Serialize(demoSession);
		} else if (path.Contains("drivers")) {
			var demoDrivers = new List<OpenF1Driver> {
				new() { DriverNumber = 33, FullName = "Max Verstappen", NameAcronym = "VER", TeamName = "Red Bull Racing", TeamColour = "3671C6" },
				new() { DriverNumber = 1, FullName = "Lando Norris", NameAcronym = "NOR", TeamName = "McLaren", TeamColour = "FF8000" }
			};
			content = JsonSerializer.Serialize(demoDrivers);
		} else if (path.Contains("track_status")) {
			_tickCount++; // The worker runs every 5 seconds, so we increment the tick count on each call to simulate time passing

			var trackClear = new[] { new { status = "1", message = "Track Clear" } };
			var yellowFlag = new[] { new { status = "2", message = "Car on track" } };
			var redFlag = new[] { new { status = "5", message = "Session Suspended" } };

			if (_tickCount < 3) {
				content = JsonSerializer.Serialize(trackClear);
			} else if (_tickCount < 6) {
				content = JsonSerializer.Serialize(yellowFlag);
			} else if (_tickCount < 9) {
				content = JsonSerializer.Serialize(redFlag);
			} else {
				content = JsonSerializer.Serialize(trackClear);
			}

			// after 12 ticks (1 minute), we end the demo session and switch back to standby mode
			if (_tickCount >= 12) {
				_sessionState.IsDemoMode = false;
				_sessionState.IsActive = false; // back to normal standby
			}
		} else if (path.Contains("position")) {
			var landoP1 = new[] { new { driver_number = 1 } };
			var maxP1 = new[] { new { driver_number = 33 } };

			if (_tickCount < 4) {
				content = JsonSerializer.Serialize(landoP1);
			} else {
				content = JsonSerializer.Serialize(maxP1);
			}
		}

		return Task.FromResult(new HttpResponseMessage {
			StatusCode = System.Net.HttpStatusCode.OK,
			Content = new StringContent(content)
		});
	}
}