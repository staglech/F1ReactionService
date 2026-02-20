using MQTTnet;
using System.Threading.Channels;

namespace F1ReactionService;

public class MqttWorker : BackgroundService {
	private readonly ILogger<MqttWorker> _logger;
	private readonly ChannelReader<RaceEvent> _channelReader;
	private readonly IConfiguration _config; // Konfiguration hinzufügen
	private readonly IMqttClient _mqttClient;

	public MqttWorker(ILogger<MqttWorker> logger, Channel<RaceEvent> channel, IConfiguration config) {
		_logger = logger;
		_channelReader = channel.Reader;
		_config = config;

		var mqttFactory = new MqttClientFactory();
		_mqttClient = mqttFactory.CreateMqttClient();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		// Werte aus den Environment-Variablen lesen
		var server = _config["MQTT_SERVER"] ?? "10.10.10.89";
		var portString = _config["MQTT_PORT"] ?? "1883";
		var user = _config["MQTT_USER"];
		var pass = _config["MQTT_PASSWORD"];

		int.TryParse(portString, out var port);

		var mqttOptionsBuilder = new MqttClientOptionsBuilder()
			.WithTcpServer(server, port);

		// Nur Credentials hinzufügen, wenn User und Passwort gesetzt sind
		if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(pass)) {
			mqttOptionsBuilder.WithCredentials(user, pass);
		}

		var mqttOptions = mqttOptionsBuilder.Build();

		_logger.LogInformation("🌐 MqttWorker versucht Verbindung zu {server}:{port}...", server, port);

		// ... Rest der Verbindungslogik und die Channel-Schleife (wie gehabt) ...
		await _mqttClient.ConnectAsync(mqttOptions, stoppingToken);

		await foreach (var raceEvent in _channelReader.ReadAllAsync(stoppingToken)) {
			if (_mqttClient.IsConnected) {
				var message = new MqttApplicationMessageBuilder()
					.WithTopic(raceEvent.Topic)
					.WithPayload(raceEvent.Payload)
					.Build();

				await _mqttClient.PublishAsync(message, stoppingToken);
				_logger.LogInformation("📤 MQTT gesendet an {topic}", raceEvent.Topic);
			}
		}
	}
}