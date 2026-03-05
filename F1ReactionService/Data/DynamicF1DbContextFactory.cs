using Microsoft.EntityFrameworkCore;

namespace F1ReactionService.Data;

/// <summary>
/// Provides a factory for creating instances of F1DbContext configured for the current year. Implements
/// IDbContextFactory<F1DbContext> to support design-time and runtime creation of database contexts.
/// </summary>
/// <remarks>Each context instance is configured to use a SQLite database file named according to the current UTC
/// year (e.g., 'f1_season_2024.db'). The factory ensures that the database and its containing directory are created
/// only once per year to optimize performance. This class is thread-safe and suitable for use in environments where
/// multiple context instances may be created concurrently.</remarks>
public class DynamicF1DbContextFactory : IDbContextFactory<F1DbContext> {
	private static int _initializedYear = 0;
	private static readonly object _lock = new();

	/// <summary>
	/// Creates and configures a new instance of the F1DbContext for the current year using a SQLite database file.
	/// </summary>
	/// <remarks>If the database for the current year does not exist, it is created automatically. This method is
	/// thread-safe and ensures that the database schema is initialized only once per year.</remarks>
	/// <returns>A new F1DbContext instance connected to the SQLite database for the current year.</returns>
	public F1DbContext CreateDbContext() {
		var currentYear = DateTime.UtcNow.Year;
		var optionsBuilder = new DbContextOptionsBuilder<F1DbContext>();

		optionsBuilder.UseSqlite($"Data Source=data/f1_season_{currentYear}.db");
		var context = new F1DbContext(optionsBuilder.Options);

		if (_initializedYear != currentYear) {
			lock (_lock) {
				// Double-Check-Locking (seems to be best practice)
				if (_initializedYear != currentYear) {
					if (!Directory.Exists("data")) {
						Directory.CreateDirectory("data");
					}
					context.Database.EnsureCreated();
					_initializedYear = currentYear;
				}
			}
		}

		return context;
	}
}