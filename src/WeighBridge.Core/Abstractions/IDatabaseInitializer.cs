namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Prepares the database for use: creates the file, applies pending migrations and
/// reports what happened so startup can be logged accurately.
/// </summary>
public interface IDatabaseInitializer
{
    /// <summary>Creates and migrates the database.</summary>
    Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="IDatabaseInitializer.InitializeAsync"/>.</summary>
/// <param name="Succeeded">False when initialisation failed; the app still starts.</param>
/// <param name="Message">Summary suitable for the log and the status tooltip.</param>
/// <param name="AppliedMigrations">Names of migrations applied during this call.</param>
/// <param name="Error">The failure, when one occurred.</param>
public sealed record DatabaseInitializationResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> AppliedMigrations,
    Exception? Error = null)
{
    /// <summary>Creates a successful result.</summary>
    public static DatabaseInitializationResult Success(string message, IReadOnlyList<string>? applied = null)
        => new(true, message, applied ?? []);

    /// <summary>Creates a failed result.</summary>
    public static DatabaseInitializationResult Failure(string message, Exception? error = null)
        => new(false, message, [], error);
}
