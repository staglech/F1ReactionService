using System.Text.Json.Serialization;

namespace F1ReactionService.Model;

/// <summary>
/// Represents a Formula 1 driver and associated team information as provided by the OpenF1 API.
/// </summary>
/// <remarks>This class is typically used to deserialize driver data retrieved from the OpenF1 API. All properties
/// correspond to fields in the API response and provide identifying and team-related information for a
/// driver.</remarks>
public class OpenF1Driver {

	/// <summary>
	/// Gets or sets the unique number assigned to the driver.
	/// </summary>
	[JsonPropertyName("driver_number")]
	public int DriverNumber { get; set; }

	/// <summary>
	/// Gets or sets the full name of the individual.
	/// </summary>
	[JsonPropertyName("full_name")]
	public string FullName { get; set; }

	/// <summary>
	/// Gets or sets the acronym representing the name of the entity.
	/// </summary>
	[JsonPropertyName("name_acronym")]
	public string NameAcronym { get; set; }

	/// <summary>
	/// Gets or sets the name of the team.
	/// </summary>
	[JsonPropertyName("team_name")]
	public string TeamName { get; set; }

	/// <summary>
	/// Gets or sets the team color associated with the entity.
	/// </summary>
	[JsonPropertyName("team_colour")]
	public string TeamColour { get; set; }
}