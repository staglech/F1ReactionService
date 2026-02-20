using MQTTnet;
using System.Threading.Channels;

namespace F1ReactionService;

public class MqttWorker : BackgroundService {
	private readonly ILogger<MqttWorker> _logger;
	private readonly ChannelReader<RaceEvent> _channelReader;
	private readonly IMqttClient _mqttClient;

	public MqttWorker(ILogger<MqttWorker> logger, Channel<RaceEvent> channel) {
		_logger = logger;
		_channelReader = channel.Reader; // Er liest nur aus dem Rohr!

		var mqttFactory = new MqttClientFactory();
		_mqttClient = mqttFactory.CreateMqttClient();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		_logger.LogInformation("🌐 MqttWorker gestartet. Verbinde zu Mosquitto...");

		var mqttOptions = new MqttClientOptionsBuilder()
			.WithTcpServer("10.10.10.89", 1883)
			.Build();

		await _mqttClient.ConnectAsync(mqttOptions, stoppingToken);

		// Das ist die absolute Magie von Channels in C#:
		// Die Schleife wartet asynchron, bis ein Item im Channel ist, ohne die CPU zu belasten.
		await foreach (var raceEvent in _channelReader.ReadAllAsync(stoppingToken)) {
			if (_mqttClient.IsConnected) {
				var message = new MqttApplicationMessageBuilder()
					.WithTopic(raceEvent.Topic)
					.WithPayload(raceEvent.Payload)
					.Build();

				await _mqttClient.PublishAsync(message, stoppingToken);
				_logger.LogInformation("📤 MQTT erfolgreich gesendet an {topic}", raceEvent.Topic);
			} else {
				_logger.LogWarning("⚠️ MQTT nicht verbunden! Event konnte nicht gesendet werden.");
				// Hier könnte man später eine Reconnect-Logik einbauen
			}
		}
	}
}