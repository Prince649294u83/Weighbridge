namespace WeighBridge.Core.Security;

/// <summary>
/// Authenticates users against the system.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Shortest password the application will accept when one is being set.
    /// </summary>
    /// <remarks>
    /// Declared here rather than in the implementation so the dialog that collects a new
    /// password and the service that enforces the rule cannot drift apart. The dialog's
    /// check is a courtesy to the operator; the service's check is the control.
    /// </remarks>
    const int MinimumPasswordLength = 8;

    /// <summary>
    /// Attempts to sign in the user. If successful, updates the <see cref="IPermissionService"/>
    /// with the authenticated operator's identity.
    /// </summary>
    /// <returns>True if authentication succeeded, false if credentials were invalid.</returns>
    Task<bool> AuthenticateAsync(string username, string password);

    /// <summary>
    /// Signs the current operator out, leaving the terminal unauthenticated.
    /// </summary>
    /// <remarks>
    /// Paired with <see cref="AuthenticateAsync"/> so both ends of a session are recorded in
    /// the audit trail by the same service. Clearing the identity is delegated to
    /// <see cref="IPermissionService.SignOut"/>; what happens here is the evidence.
    /// </remarks>
    void SignOut();

    /// <summary>
    /// True when the database holds no user accounts, so nobody can sign in yet and the
    /// application must collect an administrator before it can be used.
    /// </summary>
    /// <remarks>
    /// Asks for the absence of any row, not the absence of an <em>enabled</em> row.
    /// "No usable account" would be a backdoor: disabling the last administrator would
    /// then let the next person to start the application appoint themselves one.
    /// </remarks>
    Task<bool> RequiresInitialSetupAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the first administrator account and signs it in.
    /// </summary>
    /// <remarks>
    /// Refuses if any account already exists, so this cannot be used to add a second
    /// unauthorised administrator - the caller's own check of
    /// <see cref="RequiresInitialSetupAsync"/> is a UI decision, not the control.
    /// </remarks>
    /// <returns>True if the account was created and is now the signed-in operator.</returns>
    Task<bool> CreateInitialAdministratorAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hashes a password for storage. The result carries its own salt and cost factor.
    /// </summary>
    string HashPassword(string password);

    /// <summary>
    /// Checks a password against a stored hash produced by <see cref="HashPassword"/>.
    /// </summary>
    /// <remarks>
    /// A salted hash cannot be checked by re-hashing and comparing strings, so verification
    /// is a separate operation rather than the caller's business.
    /// </remarks>
    bool VerifyPassword(string password, string storedHash);
}
