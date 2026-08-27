using System.Security.Cryptography;
using System.Text;
using Fido2NetLib.Objects;

namespace Passless.Api.Features.WebAuthn;

/// <summary>
/// Plausible allowCredentials entries for a username that does not exist.
/// </summary>
/// <remarks>
/// Without these, asking for assertion options is an account oracle: a real
/// account comes back with descriptors and an imaginary one comes back with an
/// empty list, and both are a 200. The endpoint would answer "does this person
/// bank here?" to anyone who asked.
///
/// The descriptors are derived, not random, and that is the point. A random set
/// would differ on every request, so asking twice and comparing would separate
/// the invented accounts from the real ones immediately. Keyed derivation makes
/// the same unknown username produce the same descriptors forever, exactly as a
/// real account does.
///
/// Honest about the limit: this raises the cost of enumeration, it does not end
/// it. Someone holding a real account can compare against known-good structure,
/// and a decoy set will never satisfy an authenticator, so a determined attacker
/// who completes the ceremony still learns something. It closes the cheap oracle,
/// which is the one that gets scraped.
/// </remarks>
internal static class CredentialDecoys
{
    /// <summary>Matches the length our own authenticators produce.</summary>
    private const int CredentialIdLength = 32;

    public static IReadOnlyList<PublicKeyCredentialDescriptor> For(
        string normalizedUsername,
        byte[] decoyKey)
    {
        var seed = HMACSHA256.HashData(decoyKey, Encoding.UTF8.GetBytes(normalizedUsername));

        // One or two, because that is what real accounts overwhelmingly have.
        // A fixed count would itself be a tell.
        var count = 1 + (seed[0] % 2);

        var descriptors = new PublicKeyCredentialDescriptor[count];
        for (var index = 0; index < count; index++)
        {
            var material = new byte[seed.Length + 1];
            seed.CopyTo(material, 0);
            material[^1] = (byte)index;

            descriptors[index] = new PublicKeyCredentialDescriptor(
                PublicKeyCredentialType.PublicKey,
                HMACSHA256.HashData(decoyKey, material)[..CredentialIdLength],
                [AuthenticatorTransport.Internal, AuthenticatorTransport.Hybrid]);
        }

        return descriptors;
    }
}
