using System.ComponentModel.DataAnnotations;

namespace F1ReactionService.Data.Models;

/// <summary>
/// Represents a telemetry event that occurs during a race session, including its timing, topic, and associated data
/// payload.
/// </summary>
/// <remarks>Each instance of this class corresponds to a single event within a race session, identified by its
/// session ID and topic. The event's timing is specified relative to the session's start point using the
/// synchronization offset. The payload contains the event data in JSON format, which may vary depending on the
/// topic.</remarks>
public class RaceEvent {

	/// <summary>
	/// Gets the unique identifier for the entity.
	/// </summary>
	[Key]
	public int Id { get; init; }

	/// <summary>
	/// Gets the unique identifier for the session.
	/// </summary>
	public required string SessionId { get; init; }

	/// <summary>
	/// Gets the synchronization offset, in milliseconds, to apply when aligning data streams.
	/// </summary>
	public required long SyncOffsetMs { get; init; }

	/// <summary>
	/// Gets the topic identifier associated with the message.
	/// </summary>
	/// <remarks>The topic typically represents a hierarchical path used for message routing or categorization, such
	/// as in publish-subscribe messaging systems. The format and meaning of the topic string may vary depending on the
	/// messaging protocol or application context.</remarks>
	public required string Topic { get; init; }

	/// <summary>
	/// Gets the raw JSON payload associated with this instance.
	/// </summary>
	public required string Payload { get; init; }
}