namespace Passless.Core.Entities;

/// <summary>Why a token family stopped being usable.</summary>
/// <remarks>
/// Persisted by name, not by ordinal. Inserting a member into the middle of an
/// integer-backed enum silently rewrites the meaning of every historical row,
/// and in a table that exists to explain what happened, that is a failure you
/// cannot detect afterwards.
/// </remarks>
public enum TokenFamilyInvalidationReason
{
    /// <summary>A consumed refresh token was presented again — treated as theft.</summary>
    TokenReuseDetected,
    UserSignedOut,
    SessionRevoked,
    UserDisabled,
    Expired,
}

public enum AuditSeverity
{
    Info,
    Notice,
    Critical,
}

/// <summary>The set of things worth writing down. Persisted by name.</summary>
public enum AuditEventType
{
    UserRegistered,
    UserDisabled,

    CredentialRegistered,
    CredentialRemoved,
    CredentialUsed,

    ChallengeIssued,
    ChallengeRejected,
    /// <summary>A registration ceremony was refused; metadata carries the reason.</summary>
    RegistrationRejected,
    AssertionRejected,
    /// <summary>Presented counter did not exceed the stored one — possible clone.</summary>
    SignCounterRegression,

    RefreshTokenIssued,
    RefreshTokenRotated,
    /// <summary>A refresh was refused; metadata carries the reason.</summary>
    RefreshTokenRejected,
    RefreshTokenReuseDetected,
    TokenFamilyInvalidated,

    SessionCreated,
    SessionRevoked,

    PasswordSignInUsed,
}
