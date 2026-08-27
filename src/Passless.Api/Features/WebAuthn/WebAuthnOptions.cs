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
}
