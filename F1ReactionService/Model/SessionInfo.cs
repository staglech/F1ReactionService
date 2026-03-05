namespace F1ReactionService.Model;

/// <summary>
/// Represents information about a session, including its name, type, and status.
/// </summary>
/// <remarks>Use this class to track and manage the state of a session, such as whether it is a race, live, stale,
/// or newly started. The properties provide details that can be used to determine how to handle or display session data
/// in client applications.</remarks>
public class SessionInfo {

	private string _sessionName = "Unknown";

	/// <summary>
	/// Gets or sets the name of the current session.
	/// </summary>
	public string SessionName {
		get { return _sessionName; }
		set {
			IsNewSession = value != _sessionName;
			_sessionName = value;
		}
	}

	/// <summary>
	/// Gets or sets a value indicating whether the event is classified as a race.
	/// </summary>
	public bool IsRace { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the current instance is active and available for use.
	/// </summary>
	public bool IsLive { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the data is considered stale.
	/// </summary>
	public bool IsStale { get; set; }

	/// <summary>
	/// Gets a value indicating whether the current session is newly created.
	/// </summary>
	public bool IsNewSession { get; private set; }

	/// <summary>
	/// Gets or sets the session key used to identify the current user session.
	/// </summary>
	public string SessionKey { get; set; } = string.Empty;
}
