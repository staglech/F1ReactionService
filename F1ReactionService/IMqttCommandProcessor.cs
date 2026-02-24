namespace F1ReactionService;

/// <summary>
/// Defines a contract for processing MQTT commands.
/// </summary>
/// <remarks>Implementations of this interface are responsible for handling MQTT command strings and executing the
/// corresponding actions. This interface is typically used to decouple command parsing from command execution logic in
/// MQTT-based applications.</remarks>
public interface IMqttCommandProcessor {

	/// <summary>
	/// Processes the specified command string and performs the associated action.
	/// </summary>
	/// <param name="command">The command to process. Cannot be null or empty.</param>
	void ProcessCommand(string command);
}
