using System.Text.Json;

namespace Passless.Api.Features.WebAuthn;

internal enum ClientDataVerdict
{
    Ok,
    Malformed,
    WrongCeremonyType,
    OriginNotAllowed,
}

/// <summary>
/// An independent check of clientDataJSON, run before the response reaches
/// Fido2NetLib.
///
/// The library performs its own origin comparison, and this does not replace
/// it. It exists because the origin check is the single control standing
/// between this server and a phishing site running the same ceremony from a
/// domain the user was tricked into visiting, and delegating the whole of it to
/// a configuration value passed into a library — where a typo, an empty set or
/// a future default would silently widen it — leaves nothing in this codebase
/// that visibly performs the check. Two independent comparisons against the
/// same explicit allowlist cost one string compare.
/// </summary>
internal static class ClientDataInspector
{
    public static ClientDataVerdict Inspect(
        ReadOnlySpan<byte> clientDataJson,
        string expectedType,
        IReadOnlyCollection<string> allowedOrigins)
    {
        string? type;
        string? origin;

        try
        {
            using var document = JsonDocument.Parse(clientDataJson.ToArray());
            var root = document.RootElement;
            type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            origin = root.TryGetProperty("origin", out var o) ? o.GetString() : null;
        }
        catch (JsonException)
        {
            return ClientDataVerdict.Malformed;
        }

        if (type is null || origin is null)
        {
            return ClientDataVerdict.Malformed;
        }

        // webauthn.create and webauthn.get are not interchangeable. Accepting
        // either would let a signature collected during one ceremony be replayed
        // into the other.
        if (!string.Equals(type, expectedType, StringComparison.Ordinal))
        {
            return ClientDataVerdict.WrongCeremonyType;
        }

        // Ordinal, and against the serialised origin exactly as the browser
        // wrote it.
        //
        // Origins compare as a tuple of scheme, host and port, and the host is
        // compared as a string — not resolved. This is why `http://localhost:4200`
        // and `http://127.0.0.1:4200` are two different origins to WebAuthn even
        // though they reach the same server over the same loopback interface: the
        // browser never asks what an address resolves to, it compares the text.
        //
        // The practical consequence is that a passkey registered while browsing
        // "localhost" simply does not exist when the same person opens
        // "127.0.0.1", and vice versa. It also runs deeper than a string compare,
        // because the RP ID must be a registrable domain suffix of the origin's
        // host: "localhost" is a valid RP ID for the host localhost, while an IP
        // literal has no registrable domain at all and can never serve as one.
        // Listing both here does not make them one origin — it permits two, and
        // credentials still do not cross between them.
        var allowed = allowedOrigins.Contains(origin, StringComparer.Ordinal);

        return allowed ? ClientDataVerdict.Ok : ClientDataVerdict.OriginNotAllowed;
    }
}
