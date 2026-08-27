namespace Passless.Core.Entities;

public sealed class User
{
    // EF materialises through this; the null-forgiving initialisers below exist
    // for the same reason. Application code must use the public constructor.
    private User()
    {
    }

    public User(Guid id, string username, string displayName, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Id = id;
        Username = username;
        NormalizedUsername = NormalizeUsername(username);
        DisplayName = displayName;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Also serves as the WebAuthn user handle. A v4 GUID carries no personal
    /// information, which the specification requires of the handle — a username
    /// or email address there would be readable from any authenticator the
    /// credential is synced to.
    /// </summary>
    public Guid Id { get; private set; }

    public string Username { get; private set; } = null!;

    /// <summary>
    /// Case-folded form carrying the unique index. Kept as a real column rather
    /// than a non-deterministic collation, which would disable index use for
    /// pattern matching, or citext, which needs an extension installed.
    /// </summary>
    public string NormalizedUsername { get; private set; } = null!;

    public string DisplayName { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DisabledAt { get; private set; }

    public bool IsDisabled => DisabledAt is not null;

    public void Disable(DateTimeOffset at)
    {
        // Idempotent: the first disabling is the one that matters, and a second
        // call should not move the timestamp away from when access was lost.
        DisabledAt ??= at;
    }

    public void Rename(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName;
    }

    public static string NormalizeUsername(string username) =>
        username.Trim().ToUpperInvariant();
}
