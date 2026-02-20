namespace F1ReactionService;

public record DriverInfo(string Name, string Abbreviation, string TeamKey);
// Jetzt mit den zugehörigen Fahrernummern
public record TeamInfo(string Name, string ColorHex, int[] DriverNumbers);

public static class F1Registry {
	public static readonly Dictionary<string, TeamInfo> Teams = new()
	{
		{ "red_bull", new TeamInfo("Red Bull Racing", "#4781D7", [3, 6]) },
		{ "ferrari", new TeamInfo("Ferrari", "#ED1131", [16, 44]) },
		{ "mercedes", new TeamInfo("Mercedes", "#00D7B6", [12, 63]) },
		{ "mclaren", new TeamInfo("McLaren", "#F47600", [1, 81]) },
		{ "aston_martin", new TeamInfo("Aston Martin", "#229971", [14, 18]) },
		{ "alpine", new TeamInfo("Alpine", "#00A1E8", [10, 43]) },
		{ "racing_bulls", new TeamInfo("RB", "#6C98FF", [30, 41]) },
		{ "haas", new TeamInfo("HAAS", "#9C9FA2", [31, 87]) },
		{ "audi", new TeamInfo("Audi", "#F50537", [5, 27]) },
		{ "williams", new TeamInfo("Williams", "#1868DB", [23, 55]) },
		{ "cadillac", new TeamInfo("Cadillac", "#D3D3D3", [11, 77]) }
	};

	public static readonly Dictionary<int, DriverInfo> Drivers = new()
	{
		{ 1, new DriverInfo("Lando Norris", "NOR", "mclaren") },
		{ 81, new DriverInfo("Oscar Piastri", "PIA", "mclaren") },
		{ 3, new DriverInfo("Max Verstappen", "VER", "red_bull") },
		{ 6, new DriverInfo("Isack Hadjar", "HAD", "red_bull") },
		{ 16, new DriverInfo("Charles Leclerc", "LEC", "ferrari") },
		{ 44, new DriverInfo("Lewis Hamilton", "HAM", "ferrari") },
		{ 12, new DriverInfo("Kimi Antonelli", "ANT", "mercedes") },
		{ 63, new DriverInfo("George Russell", "RUS", "mercedes") },
		{ 14, new DriverInfo("Fernando Alonso", "ALO", "aston_martin") },
		{ 18, new DriverInfo("Lance Stroll", "STR", "aston_martin") },
		{ 10, new DriverInfo("Pierre Gasly", "GAS", "alpine") },
		{ 43, new DriverInfo("Franco Colapinto", "COL", "alpine") },
		{ 30, new DriverInfo("Liam Lawson", "LAW", "racing_bulls") },
		{ 41, new DriverInfo("Arvid Lindblad", "LIN", "racing_bulls") },
		{ 31, new DriverInfo("Esteban Ocon", "OCO", "haas") },
		{ 87, new DriverInfo("Oliver Bearman", "BEA", "haas") },
		{ 5, new DriverInfo("Gabriel Bortoleto", "BOR", "audi") },
		{ 27, new DriverInfo("Nico Hülkenberg", "HUL", "audi") },
		{ 55, new DriverInfo("Carlos Sainz", "SAI", "williams") },
		{ 23, new DriverInfo("Alexander Albon", "ALB", "williams") },
		{ 11, new DriverInfo("Sergio Perez", "PER", "cadillac") },
		{ 77, new DriverInfo("Valtteri Bottas", "BOT", "cadillac") }
	};
}