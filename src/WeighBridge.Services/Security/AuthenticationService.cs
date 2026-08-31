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
/// Implements authentication against the database with persistent account lockout,
/// password expiration tracking, and automatic upgrading of legacy password hashes.
/// </summary>
public sealed class AuthenticationService : IAuthenticationService
{
    private const string Pbkdf2Prefix = "pbkdf2-sha256";
    private const int Pbkdf2Iterations = 600_000;
    private const int SaltBytes = 16;
    private const int DigestBytes = 32;

    private readonly Func<IUnitOfWork> _unitOfWork;
    private readonly IPermissionService _permissionService;
    private readonly IApplicationLogger _logger;
    private readonly IAuditLogger _audit;
    private readonly SecurityOptions _securityOptions;

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

        var now = DateTime.UtcNow;
        await using var unitOfWork = _unitOfWork();
        var userRepo = unitOfWork.Repository<User>();

        var users = await userRepo.FindAsync(u => u.Username.ToLower() == username.ToLower());
        var user = users.FirstOrDefault();

        if (user == null || !user.IsActive)
        {
            _logger.Warning("Failed authentication attempt for unknown or inactive user: {Username}", username);
            _audit.RecordFailed("SignedIn", "User", "User is unknown or inactive.", username.Trim());
            return false;
        }

        // 1. Persistent Lockout Check
        if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > now)
        {
            var remaining = (user.LockoutUntilUtc.Value - now).TotalSeconds;
            _logger.Warning(
                "Sign-in for {Username} refused: account locked for another {Seconds:F0}s after repeated failures",
                user.Username,
                remaining);
            _audit.RecordFailed("SignedIn", "User", "Account temporarily locked after repeated failed sign-ins.", user.Username);
            return false;
        }

        // Expired lockout resets automatically
        if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value <= now)
        {
            user.ResetFailedAccess();
        }

        // 2. Verify Password
        if (!VerifyPassword(password, user.PasswordHash))
        {
            bool lockedNow = user.RecordFailedAccess(
                _securityOptions.EffectiveMaxFailedSignIns,
                _securityOptions.EffectiveLockout);

            userRepo.Update(user);
            await unitOfWork.SaveChangesAsync().ConfigureAwait(false);

            if (lockedNow)
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

            await Task.Delay(Random.Shared.Next(75, 200)).ConfigureAwait(false);
            return false;
        }

        // 3. Password Verification Succeeded -> Reset Failure Counter
        user.ResetFailedAccess();

        // 4. Password Expiration Policy Check
        if (user.PasswordChangedAtUtc.AddDays(_securityOptions.RequirePasswordChangeDays) < now)
        {
            user.SetMustChangePassword(true);
            _logger.Information("User {Username}'s password has expired and must be changed.", user.Username);
        }

        // 5. Automatic Password Hash Upgrade (from legacy unsalted to PBKDF2)
        if (IsLegacyDigest(user.PasswordHash))
        {
            user.ChangePassword(HashPassword(password));
            _logger.Information(
                "User {Username}'s password was upgraded from the pre-salt format to the current salted hash during sign-in",
                user.Username);
            _audit.Record("PasswordHashUpgraded", "User", user.Username,
                "Legacy unsalted SHA-256 replaced by PBKDF2 at first post-upgrade sign-in.");
        }

        userRepo.Update(user);
        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);

        SignIn(user);

        _logger.Information("User {Username} authenticated successfully", user.Username);
        _audit.Record("SignedIn", "User", user.Username);
        return true;
    }

    /// <inheritdoc />
    public void SignOut()
    {
        var operatorIdentity = _permissionService.CurrentOperator;
        _audit.Record("SignedOut", "User", operatorIdentity.UserName);
        _permissionService.SignOut();
        _logger.Information("User {Username} signed out", operatorIdentity.UserName);
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
            return false;
        }

        await using var unitOfWork = _unitOfWork();
        var repo = unitOfWork.Repository<User>();

        if (await repo.CountAsync(cancellationToken: cancellationToken) > 0)
        {
            return false;
        }

        var admin = User.Create(
            username.Trim(),
            username.Trim(),
            HashPassword(password),
            Roles.Administrator.Name);

        await repo.AddAsync(admin, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.Information("Initial administrator account created: {Username}", admin.Username);
        _audit.Record("UserCreated", "User", admin.Username, "Created during initial application setup.");

        SignIn(admin);
        return true;
    }

    /// <inheritdoc />
    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            DigestBytes);

        return $"{Pbkdf2Prefix}${Pbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <inheritdoc />
    public bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        if (storedHash.StartsWith(Pbkdf2Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 4) return false;

            if (!int.TryParse(parts[1], out int iterations)) return false;

            try
            {
                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] expectedHash = Convert.FromBase64String(parts[3]);

                byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(password),
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256,
                    DigestBytes);

                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // Legacy Unsalted SHA-256 fallback
        if (IsLegacyDigest(storedHash))
        {
            try
            {
                byte[] expected = Convert.FromBase64String(storedHash);
                byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(password));
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsLegacyDigest(string hash)
    {
        return !hash.StartsWith(Pbkdf2Prefix, StringComparison.OrdinalIgnoreCase) &&
               hash.Length == 44; // Base64 length of 32-byte SHA256
    }

    private void SignIn(User user)
    {
        var role = Roles.FromName(user.RoleName) ?? Roles.ReadOnly;
        var identity = new OperatorIdentity(user.Username, user.DisplayName, role);
        _permissionService.SetOperator(identity);
    }
}
