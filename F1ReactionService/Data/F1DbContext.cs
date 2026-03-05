using F1ReactionService.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace F1ReactionService.Data;

/// <summary>
/// Represents the Entity Framework Core database context for managing Formula 1 race sessions and events.
/// </summary>
/// <remarks>This context provides access to race session and event data through the Sessions and Events DbSet
/// properties. The model is configured to enforce referential integrity between sessions and events, and includes
/// indexing for efficient querying. This class is intended to be used with dependency injection in ASP.NET Core or
/// other .NET applications.</remarks>
/// <param name="options">The options to be used by the DbContext. Typically configured by dependency injection to specify the database
/// provider and connection settings.</param>
public class F1DbContext(DbContextOptions<F1DbContext> options) : DbContext(options) {
	public DbSet<RaceSession> Sessions => Set<RaceSession>();
	public DbSet<RaceEvent> Events => Set<RaceEvent>();

	/// <inheritdoc/>
	protected override void OnModelCreating(ModelBuilder modelBuilder) {
		base.OnModelCreating(modelBuilder);

		modelBuilder.Entity<RaceEvent>()
			.HasOne<RaceSession>()
			.WithMany(s => s.Events)
			.HasForeignKey(e => e.SessionId)
			.OnDelete(DeleteBehavior.Cascade);

		modelBuilder.Entity<RaceEvent>()
			.HasIndex(e => new { e.SessionId, e.SyncOffsetMs });
	}
}