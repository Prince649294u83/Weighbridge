using WeighBridge.Domain.Common;

namespace WeighBridge.Domain.Security;

/// <summary>
/// An operator who can log in to the weighbridge terminal.
/// </summary>
public sealed class User : EntityBase, IAggregateRoot, ISoftDeletable
{
    public const int UsernameMaxLength = 64;
    public const int DisplayNameMaxLength = 128;
    public const int PasswordHashMaxLength = 256;
    public const int RoleNameMaxLength = 64;

    private User()
    {
    }

    /// <summary>
    /// Creates a new user.
    /// </summary>
    public static User Create(string username, string displayName, string passwordHash, string roleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);

        var trimmedUsername = username.Trim();
        if (trimmedUsername.Length > UsernameMaxLength)
        {
            throw new ArgumentException($"Username must be {UsernameMaxLength} characters or fewer.", nameof(username));
        }

        var trimmedDisplayName = displayName.Trim();
        if (trimmedDisplayName.Length > DisplayNameMaxLength)
        {
            throw new ArgumentException($"Display name must be {DisplayNameMaxLength} characters or fewer.", nameof(displayName));
        }

        return new User
        {
            Username = trimmedUsername,
            DisplayName = trimmedDisplayName,
            PasswordHash = passwordHash,
            RoleName = roleName,
            IsActive = true
        };
    }

    /// <summary>Login name. Unique across the system.</summary>
    public string Username { get; private set; } = string.Empty;

    /// <summary>Name to display in the shell and audit log.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Hashed password. Never stored in plaintext.</summary>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>The role assigned to this user, matching one of the shipped Roles (Administrator, Supervisor, Operator, ReadOnly).</summary>
    public string RoleName { get; private set; } = string.Empty;

    /// <summary>Whether this user can currently log in.</summary>
    public bool IsActive { get; private set; } = true;

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTime? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }

    /// <summary>Updates the user's details.</summary>
    public void Update(string displayName, string roleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);

        var trimmedDisplayName = displayName.Trim();
        if (trimmedDisplayName.Length > DisplayNameMaxLength)
        {
            throw new ArgumentException($"Display name must be {DisplayNameMaxLength} characters or fewer.", nameof(displayName));
        }

        DisplayName = trimmedDisplayName;
        RoleName = roleName;
    }

    /// <summary>Updates the user's password hash.</summary>
    public void ChangePassword(string newPasswordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);
        PasswordHash = newPasswordHash;
    }

    /// <summary>Activates the user account.</summary>
    public void Activate()
    {
        IsActive = true;
    }

    /// <summary>Deactivates the user account.</summary>
    public void Deactivate()
    {
        IsActive = false;
    }

    public override string ToString() => $"{DisplayName} ({RoleName})";
}
