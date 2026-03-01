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
		} else if (path.Contains("intervals")) {
			object[] intervals;

			if (_tickCount < 1) {
				// Max is 1.5s behind Lando (no  override)
				intervals = [new { driver_number = 33, interval = 1.500 }];
			} else if (_tickCount < 4) {
				// Tick 1, 2, 3: Max is in the 1-Second-Window! (Override Attack Event fires)
				intervals = [new { driver_number = 33, interval = 0.600 }];
			} else {
				// >= Tick 4: Max overtook (P1). Lando is now 2 seconds behind Max.
				intervals = [
					new { driver_number = 1, interval = 2.000 },
					new { driver_number = 33, interval = (double?)null }
				];
			}
			content = JsonSerializer.Serialize(intervals);

		} else if (path.Contains("pit")) {
			// Lando under yellow flag in box
			if (_tickCount >= 5) {
				var pitData = new[] {
					new { driver_number = 1, lap = 15 } // Lando stops in round 15
				};
				content = JsonSerializer.Serialize(pitData);
			} else {
				content = "[]";
			}
		} else if (path.Contains("weather")) {
			// >= Tick 6 rain is coming
			var dryWeather = new[] { new { rainfall = 0.0 } };
			var wetWeather = new[] { new { rainfall = 1.0 } };

			content = JsonSerializer.Serialize(_tickCount >= 6 ? wetWeather : dryWeather);

		} else if (path.Contains("race_control")) {
			// Tick 7: Max Verstappen gets the fastet lap
			if (_tickCount == 7) {
				var fastestLapMsg = new[] { new { message = "FASTEST LAP - CAR 33 (VER) - 1:24.321" } };
				content = JsonSerializer.Serialize(fastestLapMsg);
			}
			// Tick 8: Lando Norris retired
			else if (_tickCount >= 8) {
				var retirementMsg = new[] { new { message = "CAR 1 (NOR) RETIRED" } };
				content = JsonSerializer.Serialize(retirementMsg);
			} else {
				content = "[]";
			}
		}

		return Task.FromResult(new HttpResponseMessage {
			StatusCode = System.Net.HttpStatusCode.OK,
			Content = new StringContent(content)
		});
	}
}