using F1ReactionService.Auth;
using F1ReactionService.Model;
using F1ReactionService.Recording;
using MQTTnet;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;

namespace F1ReactionService.Workers;

/// <summary>
/// Connects to the OpenF1 MQTT broker for real-time telemetry and event streaming (Paid Tier).
/// </summary>
public class OpenF1LiveMqttWorker(
	IConfiguration config,
	IHttpClientFactory httpClientFactory,
	Channel<RaceEvent> channel,
	ILogger<OpenF1LiveMqttWorker> logger,
	F1EventRecorder eventRecorder,
	F1SessionState sessionState,
	OpenF1TokenManager tokenManager) : BackgroundService {
	private readonly ChannelWriter<RaceEvent> _channelWriter = channel.Writer;
	private IMqttClient? _mqttClient;
	private SessionInfo _currentSessionInfo = new();
	private readonly F1RaceAnalyzer _analyzer = new();

	/// <summary>
	/// Executes the background worker process that manages the connection to the OpenF1 MQTT broker and processes live
	/// session data asynchronously.
	/// </summary>
	/// <remarks>The worker remains active until the cancellation token is triggered. It handles session state
	/// transitions, connects to the MQTT broker for live data, and processes incoming messages asynchronously. The method
	/// ensures proper cleanup and reconnection logic based on session activity.</remarks>
	/// <param name="stoppingToken">A cancellation token that can be used to request the operation to stop. The worker will monitor this token and
	/// terminate gracefully when cancellation is requested.</param>
	/// <returns>A task that represents the asynchronous execution of the worker operation.</returns>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		logger.LogInformation("OpenF1LiveMqttWorker started. Waiting for START signal...");

		var mqttFactory = new MqttClientFactory();
		_mqttClient = mqttFactory.CreateMqttClient();

		_mqttClient.ApplicationMessageReceivedAsync += async e => {
			if (!sessionState.IsActive) {
				return;
			}

			var topic = e.ApplicationMessage.Topic;
			var payloadString = e.ApplicationMessage.ConvertPayloadToString();

			try {
				await ProcessIncomingMqttMessageAsync(topic, payloadString, stoppingToken);
			} catch (Exception ex) {
				logger.LogError(ex, "Error processing OpenF1 MQTT message on topic {Topic}", topic);
			}
		};

		while (!stoppingToken.IsCancellationRequested) {
			if (!sessionState.IsActive) {
				await DisconnectMqttAsync();
				_analyzer.Reset();
				_currentSessionInfo = new SessionInfo();

				await sessionState.WakeUpSignal.WaitAsync(TimeSpan.FromMinutes(10), stoppingToken);
				continue;
			}

			await InitializeSessionAndDriversAsync(stoppingToken);
			await ConnectAndSubscribeAsync(stoppingToken);

			while (sessionState.IsActive && !stoppingToken.IsCancellationRequested) {
				await Task.Delay(5000, stoppingToken);
			}
		}

		await DisconnectMqttAsync();
	}

	/// <summary>
	/// Processes an incoming MQTT message by analyzing its topic and payload, and dispatches the resulting race events for
	/// further handling.
	/// </summary>
	/// <remarks>This method routes the message based on its topic and supports processing multiple event types,
	/// such as track status, leader changes, weather updates, race control events, intervals, and pit stops. Events are
	/// handled asynchronously and may be processed in sequence if multiple events are generated from a single
	/// message.</remarks>
	/// <param name="topic">The MQTT topic associated with the incoming message. Determines how the payload is interpreted and which analyzer
	/// logic is applied.</param>
	/// <param name="payloadString">The JSON-formatted payload of the MQTT message to be processed.</param>
	/// <param name="stoppingToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
	private async Task ProcessIncomingMqttMessageAsync(string topic, string payloadString, CancellationToken stoppingToken) {
		var jsonDoc = JsonDocument.Parse(payloadString);
		var jsonList = new List<JsonElement> { jsonDoc.RootElement };

		RaceEvent? generatedEvent = null;
		List<RaceEvent>? multipleEvents = null;

		if (topic.Contains("track_status")) {
			generatedEvent = _analyzer.ProcessTrackStatus(jsonDoc.RootElement);
			if (generatedEvent != null) {
				logger.LogInformation("LIVE FLAG CHANGE: {Payload}", generatedEvent.Payload);
			}
		} else if (topic.Contains("position")) {
			generatedEvent = _analyzer.ProcessLeader(jsonDoc.RootElement, _currentSessionInfo.IsRace, _currentSessionInfo.SessionName, _currentSessionInfo.IsLive);
		} else if (topic.Contains("weather")) {
			generatedEvent = _analyzer.ProcessWeather(jsonDoc.RootElement);
		} else if (topic.Contains("race_control")) {
			multipleEvents =
			[
				.. _analyzer.ProcessRetirements(jsonList),
				.. _analyzer.ProcessFastestLap(jsonList),
			];
		} else if (topic.Contains("intervals") && sessionState.TrackedDrivers.Count > 0) {
			multipleEvents = _analyzer.ProcessIntervals(jsonList);
		} else if (topic.Contains("pit") && sessionState.TrackedDrivers.Count > 0) {
			multipleEvents = _analyzer.ProcessPitStops(jsonList);
		}

		if (generatedEvent != null) {
			await HandleEventAsync(generatedEvent, _currentSessionInfo, stoppingToken);
		}

		if (multipleEvents != null) {
			foreach (var ev in multipleEvents) {
				await HandleEventAsync(ev, _currentSessionInfo, stoppingToken);
			}
		}
	}

	/// <summary>
	/// Processes a race event by recording it and publishing it to the live channel if applicable.
	/// </summary>
	/// <remarks>The event is recorded only if the session key is present. For driver-specific topics, the event is
	/// published live only if the driver is being tracked in the current session.</remarks>
	/// <param name="raceEvent">The race event to process. Contains the topic and payload information for the event.</param>
	/// <param name="sessionInfo">The session information associated with the event, including session key and name.</param>
	/// <param name="stoppingToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
	private async Task HandleEventAsync(RaceEvent raceEvent, SessionInfo sessionInfo, CancellationToken stoppingToken) {

		bool shouldPublishLive = true;
		if (raceEvent.Topic.StartsWith("f1/driver/")) {
			var topicParts = raceEvent.Topic.Split('/');
			if (topicParts.Length >= 3 && int.TryParse(topicParts[2], out int driverNum)) {
				shouldPublishLive = sessionState.TrackedDrivers.ContainsKey(driverNum);
			}
		}

		if (shouldPublishLive) {
			await _channelWriter.WriteAsync(raceEvent, stoppingToken);
		}
	}

	/// <summary>
	/// Initializes the current session and driver registry by retrieving the latest session and driver data from the
	/// configured HTTP client.
	/// </summary>
	/// <remarks>This method loads the latest session and driver information from a remote service and updates the
	/// internal state accordingly. If the operation fails, an error is logged and the internal state may remain
	/// unchanged.</remarks>
	/// <param name="stoppingToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
	/// <returns>A task that represents the asynchronous initialization operation.</returns>
	private async Task InitializeSessionAndDriversAsync(CancellationToken stoppingToken) {
		try {
			var client = httpClientFactory.CreateClient("OpenF1");

			var sessions = await client.GetFromJsonAsync<List<JsonElement>>("sessions?session_key=latest", stoppingToken);
			var session = sessions?.LastOrDefault();

			if (session != null && session.Value.ValueKind != JsonValueKind.Undefined) {
				_currentSessionInfo.SessionName = session.Value.GetProperty("session_name").GetString() ?? "Unknown";
				_currentSessionInfo.IsRace = _currentSessionInfo.SessionName.Contains("Race", StringComparison.OrdinalIgnoreCase);
				if (session.Value.TryGetProperty("session_key", out var keyProp)) {
					_currentSessionInfo.SessionKey = keyProp.GetInt32().ToString();
				}
			}

			var drivers = await client.GetFromJsonAsync<List<OpenF1Driver>>("drivers?session_key=latest", stoppingToken);
			if (drivers != null && drivers.Count != 0) {
				_analyzer.UpdateDriverRegistry(drivers);
				logger.LogInformation("Initial drivers and session data loaded via REST. Ready for MQTT Stream!");
			}
		} catch (Exception ex) {
			logger.LogError(ex, "Failed to initialize static data for MQTT worker.");
		}
	}

	/// <summary>
	/// Establishes a connection to the OpenF1 MQTT broker and subscribes to all available topics asynchronously.
	/// </summary>
	/// <remarks>If the required credentials or token are missing, the method logs an error and does not attempt to
	/// connect. The method logs connection status and errors using the configured logger.</remarks>
	/// <param name="stoppingToken">A cancellation token that can be used to cancel the connection and subscription operation.</param>
	/// <returns>A task that represents the asynchronous connect and subscribe operation.</returns>
	private async Task ConnectAndSubscribeAsync(CancellationToken stoppingToken) {
		var username = config["OPENF1_USERNAME"];

		var token = await tokenManager.GetTokenAsync(stoppingToken);

		if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(token)) {
			logger.LogError("Cannot connect to OpenF1 MQTT: Credentials or token missing.");
			return;
		}

		var mqttOptions = new MqttClientOptionsBuilder()
			.WithTcpServer("mqtt.openf1.org", 8883)
			.WithCredentials(username, token)
			.WithTlsOptions(new MqttClientTlsOptions { UseTls = true })
			.WithCleanSession()
			.Build();

		try {
			await _mqttClient!.ConnectAsync(mqttOptions, stoppingToken);
			logger.LogInformation("Connected to OpenF1 Live MQTT Broker!");

			await _mqttClient.SubscribeAsync("v1/#", cancellationToken: stoppingToken);
		} catch (Exception ex) {
			logger.LogError(ex, "Could not connect to OpenF1 MQTT Broker.");
		}
	}

	/// <summary>
	/// Disconnects from the mqtt broker.
	/// </summary>
	private async Task DisconnectMqttAsync() {
		if (_mqttClient != null && _mqttClient.IsConnected) {
			await _mqttClient.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection).Build());
			logger.LogInformation("🔌 Disconnected from OpenF1 Live MQTT Broker.");
		}
	}
}