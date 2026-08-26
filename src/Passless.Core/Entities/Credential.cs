namespace Passless.Core.Entities;

/// <summary>A registered WebAuthn credential — one passkey on one authenticator.</summary>
public sealed class Credential
{
    private Credential()
    {
    }

    public Credential(
        Guid id,
        Guid userId,
        byte[] credentialId,
        byte[] publicKey,
        uint signatureCounter,
        Guid aaguid,
        IEnumerable<string> transports,
        bool backupEligible,
        bool backupState,
        string? friendlyName,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(credentialId);
        ArgumentNullException.ThrowIfNull(publicKey);
        ArgumentNullException.ThrowIfNull(transports);

        if (credentialId.Length == 0)
        {
            throw new ArgumentException("Credential id must not be empty.", nameof(credentialId));
        }

        if (publicKey.Length == 0)
        {
            throw new ArgumentException("Public key must not be empty.", nameof(publicKey));
        }

        Id = id;
        UserId = userId;
        CredentialId = credentialId;
        PublicKey = publicKey;
        SignatureCounter = signatureCounter;
        Aaguid = aaguid;
        Transports = transports.ToArray();
        BackupEligible = backupEligible;
        BackupState = backupState;
        FriendlyName = friendlyName;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary>
    /// The raw WebAuthn credential ID. Unique across the whole table rather than
    /// per user: the same credential must never resolve to two accounts, and
    /// scoping the constraint to a user would allow exactly that.
    /// </summary>
    public byte[] CredentialId { get; private set; } = null!;

    /// <summary>CBOR-encoded COSE_Key. Public, but still never logged.</summary>
    public byte[] PublicKey { get; private set; } = null!;

    /// <summary>
    /// WebAuthn signCount. Unsigned 32-bit per the specification; PostgreSQL has
    /// no unsigned 32-bit type, so it is stored as bigint.
    /// </summary>
    public uint SignatureCounter { get; private set; }

    /// <summary>
    /// Authenticator model identifier. All-zero is the specification's value for
    /// "not disclosed", which most privacy-preserving authenticators send.
    /// </summary>
    public Guid Aaguid { get; private set; }

    public string[] Transports { get; private set; } = [];

    /// <summary>Authenticator data BE flag — the credential may be synced.</summary>
    public bool BackupEligible { get; private set; }

    /// <summary>Authenticator data BS flag — the credential currently is synced.</summary>
    public bool BackupState { get; private set; }

    public string? FriendlyName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>
    /// Records a successful assertion. Deliberately does not decide whether the
    /// presented counter is acceptable: that rule needs to distinguish a genuine
    /// regression from an authenticator that always reports zero, and it has to
    /// be able to raise an audit event. It lives with the assertion ceremony.
    /// </summary>
    public void RecordUse(uint presentedCounter, DateTimeOffset at)
    {
        SignatureCounter = presentedCounter;
        LastUsedAt = at;
    }

    public void Rename(string? friendlyName) => FriendlyName = friendlyName;
}
