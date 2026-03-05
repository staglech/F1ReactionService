using System.ComponentModel.DataAnnotations;

namespace F1ReactionService.Data.Models;

/// <summary>
/// Represents a race session, including its identity, season, name, start time, and associated race events.
/// </summary>
/// <remarks>This class is typically used to model a single session within a racing event, such as a Grand Prix
/// race. It includes navigation properties for use with Entity Framework Core, allowing access to related race
/// events.</remarks>
public class RaceSession {
	/// <summary>
	/// Gets the unique identifier for the entity.
	/// </summary>
	[Key]
	public required string Id { get; init; }

	/// <summary>
	/// Gets the year of the season represented by this instance.
	/// </summary>
	public required int Season { get; init; }

	/// <summary>
	/// Gets the name associated with this instance.
	/// </summary>
	public required string Name { get; init; }

	/// <summary>
	/// Gets the start time of the operation in Coordinated Universal Time (UTC).
	/// </summary>
	public required DateTimeOffset StartTimeUtc { get; init; }

	/// <summary>
	/// Gets or sets the collection of race events associated with this instance.
	/// </summary>
	public List<RaceEvent> Events { get; set; } = [];
}
