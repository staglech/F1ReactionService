namespace F1ReactionService.Model;

/// <summary>
/// Represents a message to be published to an MQTT topic, including the target topic and the message payload.
/// </summary>
/// <param name="Topic">The MQTT topic to which the message will be published. Cannot be null or empty.</param>
/// <param name="Payload">The content of the message to be sent to the specified topic. Cannot be null.</param>
public record RaceEvent(string Topic, string Payload);