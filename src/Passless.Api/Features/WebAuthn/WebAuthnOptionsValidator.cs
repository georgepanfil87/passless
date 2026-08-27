using Microsoft.Extensions.Options;

namespace Passless.Api.Features.WebAuthn;

/// <summary>
/// Runs at startup, not per request. A relying party whose RP ID does not match
/// its origins produces credentials that verify during registration and fail
/// forever afterwards, so this must fail the boot rather than the ceremony.
/// </summary>
internal sealed class WebAuthnOptionsValidator : IValidateOptions<WebAuthnOptions>
{
    public ValidateOptionsResult Validate(string? name, WebAuthnOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.RelyingPartyId))
        {
            failures.Add("WebAuthn:RelyingPartyId must be set.");
        }

        if (options.Origins.Length == 0)
        {
            failures.Add("WebAuthn:Origins must list at least one origin.");
        }

        if (options.ChallengeTimeToLive <= TimeSpan.Zero)
        {
            failures.Add("WebAuthn:ChallengeTimeToLive must be positive.");
        }

        foreach (var origin in options.Origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                failures.Add($"WebAuthn:Origins contains '{origin}', which is not an absolute URI.");
                continue;
            }

            if (!string.IsNullOrEmpty(uri.PathAndQuery.TrimEnd('/')) || uri.PathAndQuery.Length > 1)
            {
                failures.Add($"WebAuthn:Origins contains '{origin}', which has a path. An origin is scheme, host and port only.");
            }

            // The RP ID must equal the origin's effective domain or be a
            // registrable suffix of it. Checked here so a mismatch is a startup
            // failure with a readable message instead of an opaque rpIdHash
            // rejection inside the library.
            if (!IsRelyingPartyIdValidFor(options.RelyingPartyId, uri.Host))
            {
                failures.Add(
                    $"WebAuthn:RelyingPartyId '{options.RelyingPartyId}' is not '{uri.Host}' " +
                    $"nor a registrable domain suffix of it (origin '{origin}').");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsRelyingPartyIdValidFor(string relyingPartyId, string host)
    {
        if (string.Equals(relyingPartyId, host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // An IP address has no registrable domain, so it can never carry an RP ID
        // other than itself — and browsers reject that too. This is the concrete
        // reason 127.0.0.1 cannot borrow "localhost".
        if (System.Net.IPAddress.TryParse(host, out _))
        {
            return false;
        }

        return host.EndsWith('.' + relyingPartyId, StringComparison.OrdinalIgnoreCase);
    }
}
