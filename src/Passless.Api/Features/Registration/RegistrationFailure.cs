namespace Passless.Api.Features.Registration;

/// <summary>
/// Why a registration was refused.
///
/// These values are written to the audit log and never to the response. Every
/// failure below leaves the endpoint returning one indistinguishable body, so
/// that "this credential is already registered to someone else" and "your
/// challenge expired" are the same event as far as a caller can tell.
/// </summary>
internal enum RegistrationFailure
{
    /// <summary>No ceremony cookie on the request.</summary>
    CeremonyMissing,

    /// <summary>Never issued, already consumed, or expired — indistinguishable by design.</summary>
    ChallengeNotFound,

    ClientDataMalformed,
    WrongCeremonyType,
    OriginNotAllowed,

    /// <summary>The credential id already exists, whoever it belongs to.</summary>
    CredentialAlreadyRegistered,

    /// <summary>The username was taken between issuing options and verifying.</summary>
    UsernameUnavailable,

    /// <summary>Fido2NetLib rejected the attestation.</summary>
    AttestationInvalid,
}

internal static class RegistrationFailureExtensions
{
    /// <summary>
    /// An origin mismatch is the one failure here that has no innocent
    /// explanation: a well-behaved browser on an allowed origin cannot produce
    /// it. The rest are routine enough that treating them as alarms would train
    /// whoever reads this log to ignore it.
    /// </summary>
    public static Core.Entities.AuditSeverity Severity(this RegistrationFailure failure) => failure switch
    {
        RegistrationFailure.OriginNotAllowed => Core.Entities.AuditSeverity.Critical,
        RegistrationFailure.WrongCeremonyType => Core.Entities.AuditSeverity.Critical,
        RegistrationFailure.CredentialAlreadyRegistered => Core.Entities.AuditSeverity.Notice,
        RegistrationFailure.UsernameUnavailable => Core.Entities.AuditSeverity.Notice,
        _ => Core.Entities.AuditSeverity.Info,
    };

    public static string Reason(this RegistrationFailure failure) => failure switch
    {
        RegistrationFailure.CeremonyMissing => "ceremony_missing",
        RegistrationFailure.ChallengeNotFound => "challenge_not_found",
        RegistrationFailure.ClientDataMalformed => "client_data_malformed",
        RegistrationFailure.WrongCeremonyType => "wrong_ceremony_type",
        RegistrationFailure.OriginNotAllowed => "origin_not_allowed",
        RegistrationFailure.CredentialAlreadyRegistered => "credential_already_registered",
        RegistrationFailure.UsernameUnavailable => "username_unavailable",
        RegistrationFailure.AttestationInvalid => "attestation_invalid",
        _ => "unknown",
    };
}
