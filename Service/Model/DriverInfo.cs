namespace F1ReactionService.Model;

/// <summary>
/// Represents information about a racing driver, including their name, abbreviation, and associated team.
/// </summary>
/// <param name="Name">The full name of the driver.</param>
/// <param name="Abbreviation">The short abbreviation used to identify the driver, typically consisting of a few uppercase letters.</param>
/// <param name="TeamKey">The unique key or identifier for the team the driver is associated with.</param>
public record DriverInfo(string Name, string Abbreviation, string TeamKey);

