using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Application;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Security;

namespace WeighBridge.Services.Security;

/// <summary>
/// Implements authentication against the database, with local lockout and automatic
/// upgrading of legacy password hashes.
/// </summary>
public sealed class AuthenticationService : IAuthenticationService
{
    // Iterations, salt and digest sizes for new hashes. The iteration count is written into
    // every hash and read back out of it when verifying, so raising it here strengthens new
    // passwords without invalidating the ones already stored - there is no schema change and
    // no forced reset. 600,000 is the OWASP figure for PBKDF2-HMAC-SHA256 and costs a few
    // hundred milliseconds per sign-in, which an operator pays once per shift.
    private const string Pbkdf2Prefix = "pbkdf2-sha256";
    private const int Pbkdf2Iterations = 600_000;
    private const int SaltBytes = 16;
    private const int DigestBytes = 32;

    // Length of the base64 of an unsalted SHA-256 digest: the format written by builds
    // before this one.
    private const int LegacyDigestBytes = 32;

    // A factory, not an IRepository<User> and not an IUnitOfWork. This service is a
    // singleton and WeighBridgeDbContext is transient, so an injected repository would pin
    // one DbContext - and its change tracker - open for the lifetime of the process. The
    // factory also keeps reads and writes on the same tracker within one operation, which
    // is what a repository injected alongside a unit of work silently fails to do.
    private readonly Func<IUnitOfWork> _unitOfWork;
    private readonly IPermissionService _permissionService;
    private readonly IApplicationLogger _logger;
    private readonly IAuditLogger _audit;
    private readonly SecurityOptions _securityOptions;

    // Consecutive failed sign-ins per username, with the instant the lockout ends. In
    // memory on purpose: this defends the terminal in front of you, and persisting it
    // would let a stale lock file keep a shift locked out after a reboot.
    private readonly ConcurrentDictionary<string, FailedAttempt> _failures = new();

    public AuthenticationService(
        Func<IUnitOfWork> unitOfWork,
        IPermissionService permissionService,
        IApplicationLogger logger,
        IAuditLogger audit,
        IOptions<SecurityOptions> securityOptions)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _securityOptions = securityOptions?.Value ?? throw new ArgumentNullException(nameof(securityOptions));
    }

    /// <inheritdoc />
    public async Task<bool> AuthenticateAsync(string username, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        var key = NormalizeKey(username);
        var now = DateTime.UtcNow;

        if (_failures.TryGetValue(key, out var current) &&
            current.LockedUntil is { } until &&
            until > now)
        {
            _logger.Warning(
                "Sign-in for {Username} refused: account locked for another {Seconds:F0}s after repeated failures",
                username,
                (until - now).TotalSeconds);
            _audit.RecordFailed("SignedIn", "User", "Account temporarily locked after repeated failed sign-ins.", username.Trim());
            return false;
        }

        await using var unitOfWork = _unitOfWork();

        var users = await unitOfWork.Repository<User>()
            .FindAsync(u => u.Username.ToLower() == username.ToLower());

        var user = users.FirstOrDefault();
        if (user == null || !user.IsActive)
        {
            RecordFailure(key);
            _logger.Warning("Failed authentication attempt for unknown or inactive user: {Username}", username);
            return false;
        }

        if (!VerifyPassword(password, user.PasswordHash))
        {
            if (RecordFailureAndCheckLock(key))
            {
                _logger.Warning(
                    "User {Username} is now locked out after {Attempts} consecutive failed sign-ins",
                    user.Username,
                    _securityOptions.EffectiveMaxFailedSignIns);
                _audit.RecordFailed(
                    "SignedIn",
                    "User",
                    $"Locked out after {_securityOptions.EffectiveMaxFailedSignIns} consecutive failed sign-ins.",
                    user.Username);
            }
            else
            {
                _logger.Warning("Failed authentication attempt for user: {Username}", username);
                _audit.RecordFailed("SignedIn", "User", "Incorrect password.", user.Username);
            }

            // A short, jittered pause blunts automated guessing without being noticeable to
            // an operator who mistyped.
            await Task.Delay(Random.Shared.Next(75, 200)).ConfigureAwait(false);
            return false;
        }

        // A correct password clears the failure history: the operator proved themselves.
        _failures.TryRemove(key, out _);

        // A sign-in that succeeded against a digest written by an earlier build is a
        // sign-in with whatever password that build set - historically, one published in
        // the repository. Rather than leaving that weak hash in place indefinitely, the
        // successful credential is re-hashed into the current salted format right now,
        // under the operator's own authenticated session. After this first successful
        // sign-in the weak digest no longer exists anywhere.
        if (IsLegacyDigest(user.PasswordHash))
        {
            user.ChangePassword(HashPassword(password));

            var usersRepository = unitOfWork.Repository<User>();
            usersRepository.Update(user);
            await unitOfWork.SaveChangesAsync().ConfigureAwait(false);

            _logger.Information(
                "User {Username}'s password was upgraded from the pre-salt format to the current salted hash during sign-in",
                user.Username);
            _audit.Record("PasswordHashUpgraded", "User", user.Username,
                "Legacy unsalted SHA-256 replaced by PBKDF2 at first post-upgrade sign-in.");
        }

        SignIn(user);

        _audit.Record("SignedIn", "User", user.Username);
        return true;
    }

    /// <inheritdoc />
    public void SignOut()
    {
        // Read before the identity is cleared: afterwards the operator is the Windows
        // account, and an audit trail whose sign-out entry names the machine login instead
        // of whoever signed out is worse than no entry - it is a wrong one.
        var operatorIdentity = _permissionService.CurrentOperator;

        _audit.Record("SignedOut", "User", operatorIdentity.UserName);

        // Cleared after the entry is written, for the same reason: the audit store stamps
        // entries with the signed-in operator, so this order attributes the sign-out to the
        // person who did it.
        _permissionService.SignOut();

        _logger.Information("User {Username} signed out", operatorIdentity.UserName);

        // Deliberately not clearing _failures: a lockout survives a sign-out, otherwise
        // signing out would be the way round it.
    }

    /// <inheritdoc />
    public async Task<bool> RequiresInitialSetupAsync(CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        return await unitOfWork.Repository<User>().CountAsync(cancellationToken: cancellationToken) == 0;
    }

    /// <inheritdoc />
    public async Task<bool> CreateInitialAdministratorAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (password.Length < IAuthenticationService.MinimumPasswordLength)
        {
            _logger.Warning(
                "Initial administrator setup rejected: the password is shorter than {Minimum} characters",
                IAuthenticationService.MinimumPasswordLength);
            return false;
        }

        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<User>();

        // Re-checked here and not taken on the caller's word. The dialog's mode is a
        // presentation decision; this is the control that stops a second administrator
        // being created by anyone who can reach this method.
        if (await repository.CountAsync(cancellationToken: cancellationToken) != 0)
        {
            _logger.Warning("Initial administrator setup rejected: an account already exists");
            _audit.RecordFailed("AdministratorCreated", "User", "An account already exists; setup is closed.");
            return false;
        }

        var user = User.Create(
            username.Trim(),
            username.Trim(),
            HashPassword(password),
            Roles.Administrator.Name);

        await repository.AddAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.Information("Initial administrator account {Username} created during first-run setup", user.Username);
        _audit.Record("AdministratorCreated", "User", user.Username, "First-run setup.");

        SignIn(user);
        _audit.Record("SignedIn", "User", user.Username);
        return true;
    }

    /// <inheritdoc />
    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var digest = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, DigestBytes);

        return string.Join('$',
            Pbkdf2Prefix,
            Pbkdf2Iterations,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(digest));
    }

    /// <inheritdoc />
    public bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        if (IsLegacyDigest(storedHash))
        {
            return VerifyLegacyPassword(password, storedHash);
        }

        var parts = storedHash.Split('$');

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        if (!TryFromBase64(parts[2], out var salt) || !TryFromBase64(parts[3], out var expected) || expected.Length == 0)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>
    /// True when the stored value is not in this build's format, which means it was written by
    /// a build before password hashing was salted.
    /// </summary>
    private static bool IsLegacyDigest(string storedHash)
    {
        var parts = storedHash.Split('$');
        return parts.Length != 4 || parts[0] != Pbkdf2Prefix;
    }

    /// <summary>
    /// Accepts the bare base64 SHA-256 digest written by builds before password hashing was
    /// salted, exactly once: the moment such a credential verifies, the caller upgrades the
    /// stored hash and this path never runs again for that account.
    /// </summary>
    private static bool VerifyLegacyPassword(string password, string storedHash)
    {
        if (!TryFromBase64(storedHash, out var expected) || expected.Length != LegacyDigestBytes)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(password)), expected);
    }

    private static bool TryFromBase64(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private static string NormalizeKey(string username) => username.Trim().ToLowerInvariant();

    private void RecordFailure(string key)
    {
        var now = DateTime.UtcNow;

        // An expired lockout restarts the count: the penalty was served, and an operator
        // returning from one must not be one typo away from another.
        _failures.AddOrUpdate(
            key,
            _ => new FailedAttempt(1, null),
            (_, existing) => existing.LockedUntil is { } until && until > now
                ? existing
                : new FailedAttempt(existing.Count + 1, null));
    }

    private bool RecordFailureAndCheckLock(string key)
    {
        var now = DateTime.UtcNow;
        var max = _securityOptions.EffectiveMaxFailedSignIns;

        var state = _failures.AddOrUpdate(
            key,
            _ => new FailedAttempt(1, null),
            (_, existing) => existing.LockedUntil is { } until && until > now
                ? existing
                : new FailedAttempt(existing.Count + 1, null));

        if (state.Count >= max && state.LockedUntil is null)
        {
            var until = now + _securityOptions.EffectiveLockout;
            _failures[key] = state with { LockedUntil = until };
            return true;
        }

        return false;
    }

    private void SignIn(User user)
    {
        var role = Roles.FromName(user.RoleName) ?? Roles.ReadOnly;

        _permissionService.SetOperator(new OperatorIdentity(user.Username, user.DisplayName, role));

        _logger.Information("User {Username} authenticated successfully as {RoleName}", user.Username, role.Name);
    }    private sealed record FailedAttempt(int Count, DateTime? LockedUntil);
}
