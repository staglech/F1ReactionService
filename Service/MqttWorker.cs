using F1ReactionService.Model;
using MQTTnet;
using System.Text;
using System.Threading.Channels;

namespace F1ReactionService;

/// <summary>
/// Provides a background service that connects to an MQTT broker, subscribes to command topics, and publishes race
/// event messages based on received commands and session state.
/// </summary>
/// <remarks>MqttWorker manages the lifecycle of an MQTT client, handling connection, subscription, and message
/// publishing in response to race events and external commands. It listens for specific commands to control session
/// activity and calibration, and ensures that race event messages are published with appropriate timing. This class is
/// intended to be hosted as a long-running background service within an application. Thread safety and correct session
/// state management are handled internally.</remarks>
public class MqttWorker : BackgroundService {
	private readonly ILogger<MqttWorker> _logger;
	private readonly ChannelReader<RaceEvent> _channelReader;
	private readonly IConfiguration _config;
	private readonly F1SessionState _sessionState;
	private readonly IMqttClient _mqttClient;

	/// <summary>
	/// Initializes a new instance of the MqttWorker class with the specified logger, event channel, configuration, and
	/// session state.
	/// </summary>
	/// <param name="logger">The logger used to record diagnostic and operational messages for the worker.</param>
	/// <param name="channel">The channel used to receive race event messages for processing.</param>
	/// <param name="config">The configuration settings used to initialize the worker and its dependencies.</param>
	/// <param name="sessionState">The current session state information for the F1 session.</param>
	public MqttWorker(
		ILogger<MqttWorker> logger,
		Channel<RaceEvent> channel,
		IConfiguration config,
		F1SessionState sessionState) {
		_logger = logger;
		_channelReader = channel.Reader;
		_config = config;
		_sessionState = sessionState;

		var mqttFactory = new MqttClientFactory();
		_mqttClient = mqttFactory.CreateMqttClient();
	}

	/// <summary>
	/// Executes the background MQTT worker operation, managing connection, subscription, and message handling until the
	/// service is stopped.
	/// </summary>
	/// <remarks>This method establishes an MQTT connection, subscribes to command topics, and processes messages
	/// from a channel in a background loop. It responds to specific MQTT commands to control the worker's state. The
	/// method completes when the cancellation token is triggered. If the MQTT server or credentials are not configured,
	/// default values are used.</remarks>
	/// <param name="stoppingToken">A cancellation token that can be used to request the operation to stop. The operation will monitor this token and
	/// terminate when cancellation is requested.</param>
	/// <returns>A task that represents the asynchronous execution of the background operation.</returns>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		var server = _config["MQTT_SERVER"] ?? "";
		var portString = _config["MQTT_PORT"] ?? "1883";
		var user = _config["MQTT_USER"];
		var pass = _config["MQTT_PASSWORD"];
		_ = int.TryParse(portString, out var port);

		var mqttOptionsBuilder = new MqttClientOptionsBuilder()
			.WithTcpServer(server, port)
			.WithCleanSession();

		if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(pass)) {
			mqttOptionsBuilder.WithCredentials(user, pass);
		}

		var mqttOptions = mqttOptionsBuilder.Build();

		_mqttClient.ApplicationMessageReceivedAsync += async e => {
			var payloadBytes = e.ApplicationMessage.Payload;
			var command = !payloadBytes.IsEmpty ? Encoding.UTF8.GetString(payloadBytes) : string.Empty;

			switch (command) {
				case "START":
					_sessionState.IsDemoMode = false;
					_sessionState.IsActive = true;
					if (_sessionState.WakeUpSignal.CurrentCount == 0) {
						_sessionState.WakeUpSignal.Release();
					}
					_logger.LogWarning("🚀 F1-service awake! Start polling...");
					break;

				case "DEMO_START":
					_sessionState.IsDemoMode = true;
					_sessionState.IsActive = true;
					_logger.LogWarning("🎪 STARTED DEMO-MODE! Running demo script...");
					if (_sessionState.WakeUpSignal.CurrentCount == 0) {
						_sessionState.WakeUpSignal.Release();
					}

					break;

				case "STOP":
					_sessionState.IsDemoMode = false;
					_sessionState.IsActive = false;
					_sessionState.TrueDataStartTime = null;
					_logger.LogWarning("💤 F1-service moves to STANDBY.");
					break;

				case "CALIBRATE_START":
					if (_sessionState.TrueDataStartTime.HasValue) {
						_sessionState.CurrentDelay = DateTime.UtcNow - _sessionState.TrueDataStartTime.Value;
						_logger.LogWarning("⏱️ CALIBRATE: Delay set to {S}s.", _sessionState.CurrentDelay.TotalSeconds);
					}
					break;
			}
			await Task.CompletedTask;
		};

		// Create connection and start the subscription
		_logger.LogInformation("🌐 MqttWorker will connect to {Server}:{Port}...", server, port);
		await _mqttClient.ConnectAsync(mqttOptions, stoppingToken);
		await _mqttClient.SubscribeAsync("f1/service/command", cancellationToken: stoppingToken);

		await foreach (var raceEvent in _channelReader.ReadAllAsync(stoppingToken)) {
			_ = Task.Run(async () => {
				if (_sessionState.CurrentDelay > TimeSpan.Zero) {
					await Task.Delay(_sessionState.CurrentDelay, stoppingToken);
				}

				if (_mqttClient.IsConnected) {
					var message = new MqttApplicationMessageBuilder()
						.WithTopic(raceEvent.Topic)
						.WithPayload(raceEvent.Payload)
						.Build();

					await _mqttClient.PublishAsync(message, stoppingToken);
					_logger.LogInformation("📤 MQTT sends ({Delay}s delay): {Topic}",
						_sessionState.CurrentDelay.TotalSeconds, raceEvent.Topic);
				}
			}, stoppingToken);
		}
	}

}