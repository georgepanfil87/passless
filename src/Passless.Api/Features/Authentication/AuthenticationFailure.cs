using Passless.Core.Entities;

namespace Passless.Api.Features.Authentication;

/// <summary>
/// Why an assertion was refused. Recorded; never returned.
/// </summary>
internal enum AuthenticationFailure
{
    CeremonyMissing,

    /// <summary>Never issued, already consumed, or expired — one outcome by design.</summary>
    ChallengeNotFound,

    ClientDataMalformed,
    WrongCeremonyType,
    OriginNotAllowed,

    /// <summary>No credential with that id. Also covers a decoy being asserted.</summary>
    UnknownCredential,

    /// <summary>The account exists but is disabled, or the handle did not match.</summary>
    AccountUnavailable,

    /// <summary>A username was supplied and the credential belongs to somebody else.</summary>
    UserMismatch,

    /// <summary>Fido2NetLib rejected the signature, origin, RP ID hash or user presence.</summary>
    AssertionInvalid,

    /// <summary>Counter did not advance. Possible cloned authenticator.</summary>
    SignCounterRegression,
}

internal static class AuthenticationFailureExtensions
{
    /// <summary>
    /// A counter regression on an otherwise valid assertion is the strongest
    /// signal this system can produce: the signature checked out, so somebody
    /// holds the private key, and the counter says it is not the only copy.
    /// </summary>
    public static AuditSeverity Severity(this AuthenticationFailure failure) => failure switch
    {
        AuthenticationFailure.SignCounterRegression => AuditSeverity.Critical,
        AuthenticationFailure.OriginNotAllowed => AuditSeverity.Critical,
        AuthenticationFailure.WrongCeremonyType => AuditSeverity.Critical,
        AuthenticationFailure.UserMismatch => AuditSeverity.Notice,
        AuthenticationFailure.UnknownCredential => AuditSeverity.Notice,
        AuthenticationFailure.AccountUnavailable => AuditSeverity.Notice,
        _ => AuditSeverity.Info,
    };

    public static AuditEventType EventType(this AuthenticationFailure failure) =>
        failure == AuthenticationFailure.SignCounterRegression
            ? AuditEventType.SignCounterRegression
            : AuditEventType.AssertionRejected;

    public static string Reason(this AuthenticationFailure failure) => failure switch
    {
        AuthenticationFailure.CeremonyMissing => "ceremony_missing",
        AuthenticationFailure.ChallengeNotFound => "challenge_not_found",
        AuthenticationFailure.ClientDataMalformed => "client_data_malformed",
        AuthenticationFailure.WrongCeremonyType => "wrong_ceremony_type",
        AuthenticationFailure.OriginNotAllowed => "origin_not_allowed",
        AuthenticationFailure.UnknownCredential => "unknown_credential",
        AuthenticationFailure.AccountUnavailable => "account_unavailable",
        AuthenticationFailure.UserMismatch => "user_mismatch",
        AuthenticationFailure.AssertionInvalid => "assertion_invalid",
        AuthenticationFailure.SignCounterRegression => "sign_counter_regression",
        _ => "unknown",
    };
}
