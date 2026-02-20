namespace F1ReactionService;

/// <summary>
/// Represents the current state of an F1 session, including timing, delay, activity status, and synchronization
/// primitives.
/// </summary>
/// <remarks>This class is intended to encapsulate session-level state for Formula 1 data processing or monitoring
/// scenarios. It provides properties for tracking the start time of true data, the current delay, and whether the
/// session is active, as well as a semaphore for coordinating wake-up signals between threads.</remarks>
public class F1SessionState {
	/// <summary>
	/// Gets or sets the start time of the true data range, if available.
	/// </summary>
	public DateTime? TrueDataStartTime { get; set; }

	/// <summary>
	/// Gets or sets the current delay interval applied between operations.
	/// </summary>
	public TimeSpan CurrentDelay { get; set; } = TimeSpan.Zero;

	/// <summary>
	/// Gets or sets a value indicating whether the current instance is active.
	/// </summary>
	public bool IsActive { get; set; } = false;

	/// <summary>
	/// Gets a semaphore used to signal and coordinate wake-up events between threads.
	/// </summary>
	/// <remarks>This semaphore is typically used to notify a waiting thread that it should resume execution. The
	/// initial count is zero, so threads calling Wait will block until a release occurs. The maximum count is one, making
	/// it suitable for single wake-up notifications.</remarks>
	public SemaphoreSlim WakeUpSignal { get; } = new SemaphoreSlim(0, 1);
}