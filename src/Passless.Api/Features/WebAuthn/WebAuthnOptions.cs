using System.ComponentModel.DataAnnotations;

namespace Passless.Api.Features.WebAuthn;

public sealed class WebAuthnOptions
{
    public const string SectionName = "WebAuthn";

    /// <summary>
    /// The RP ID. Credentials are scoped to it, so changing this value
    /// invalidates every passkey already registered.
    /// </summary>
    [Required]
    public string RelyingPartyId { get; set; } = string.Empty;

    [Required]
    public string RelyingPartyName { get; set; } = string.Empty;

    /// <summary>
    /// Exact origins permitted to run a ceremony. No defaults and no wildcards:
    /// an origin allowlist that is inferred rather than stated is one deployment
    /// mistake away from accepting ceremonies from anywhere.
    /// </summary>
    [Required, MinLength(1)]
    public string[] Origins { get; set; } = [];

    /// <summary>
    /// How long a challenge stays usable. Short by design — it exists to bound
    /// the window in which a captured challenge is worth anything.
    /// </summary>
    public TimeSpan ChallengeTimeToLive { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Base64 key used to derive the decoy credential descriptors returned for
    /// usernames that do not exist.
    ///
    /// Not a secret in the sense a signing key is: disclosing it lets an
    /// attacker recompute the decoys and so restores the account oracle this
    /// closes, but it protects no data and signs nothing. It still belongs in
    /// the environment rather than in source, and it must not be shared between
    /// deployments.
    /// </summary>
    [Required]
    public string DecoyKey { get; set; } = string.Empty;

    private byte[]? _decoyKeyBytes;

    /// <summary>Decoded once; validated at startup, so this cannot throw later.</summary>
    public byte[] DecoyKeyBytes => _decoyKeyBytes ??= Convert.FromBase64String(DecoyKey);
}
