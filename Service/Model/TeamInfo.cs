namespace F1ReactionService.Model;

/// <summary>
/// Represents information about a racing team, including its name, color, and associated driver numbers.
/// </summary>
/// <param name="Name">The name of the team. Cannot be null or empty.</param>
/// <param name="ColorHex">The hexadecimal color code representing the team's color. Must be a valid hex color string (e.g., "#FF0000").</param>
/// <param name="DriverNumbers">An array of driver numbers associated with the team. Cannot be null.</param>
public record TeamInfo(string Name, string ColorHex, int[] DriverNumbers);
