namespace F1ReactionService;

// Ein einfaches Paket: An welches MQTT-Topic soll es gehen, und was ist der Inhalt (Payload)?
public record RaceEvent(string Topic, string Payload);