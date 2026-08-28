using System.Security.Cryptography;

namespace Passless.Infrastructure.Tokens;

/// <summary>
/// The wire format of a refresh token: <c>plrt_&lt;id&gt;.&lt;secret&gt;</c>.
/// </summary>
/// <remarks>
/// Split into a public lookup half and a secret half deliberately.
///
/// The obvious design is to store the token's hash and look rows up by it. That
/// makes the database index the thing performing the secret comparison, and a
/// b-tree descent is not constant time. Wrapping a <c>FixedTimeEquals</c> around
/// a row that was already located by its secret would be decoration, not defence.
///
/// Carrying the row id in the token means the lookup key is not a secret at all,
/// and the only comparison that decides anything is a fixed-time one over the
/// digest. The id discloses nothing on its own: it is a random GUID, and holding
/// it without the secret gets you a rejection.
///
/// The <c>plrt_</c> prefix exists so credential scanners have a literal to match
/// on if one of these ever reaches a log or a commit.
/// </remarks>
public static class RefreshTokenSecret
{
    public const string Prefix = "plrt_";

    private const int SecretByteLength = 32;

    public static (Guid Id, string Token, byte[] Hash) Create() => CreateWithId(Guid.NewGuid());

    public static (Guid Id, string Token, byte[] Hash) CreateWithId(Guid id)
    {
        var secret = RandomNumberGenerator.GetBytes(SecretByteLength);
        var token = $"{Prefix}{Encode(id.ToByteArray())}.{Encode(secret)}";
        return (id, token, Hash(secret));
    }

    public static byte[] Hash(ReadOnlySpan<byte> secret) => SHA256.HashData(secret);

    public static bool TryParse(string? token, out Guid id, out byte[] secret)
    {
        id = default;
        secret = [];

        if (string.IsNullOrEmpty(token) || !token.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var body = token.AsSpan(Prefix.Length);
        var separator = body.IndexOf('.');
        if (separator <= 0 || separator == body.Length - 1)
        {
            return false;
        }

        if (!TryDecode(body[..separator], out var idBytes) || idBytes.Length != 16)
        {
            return false;
        }

        if (!TryDecode(body[(separator + 1)..], out var secretBytes) || secretBytes.Length != SecretByteLength)
        {
            return false;
        }

        id = new Guid(idBytes);
        secret = secretBytes;
        return true;
    }

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryDecode(ReadOnlySpan<char> value, out byte[] decoded)
    {
        var padded = string.Create(value.Length + (4 - value.Length % 4) % 4, value.ToString(), (span, source) =>
        {
            source.AsSpan().CopyTo(span);
            span[source.Length..].Fill('=');
        }).Replace('-', '+').Replace('_', '/');

        decoded = [];
        var buffer = new byte[padded.Length / 4 * 3];

        if (!Convert.TryFromBase64String(padded, buffer, out var written))
        {
            return false;
        }

        decoded = buffer[..written];
        return true;
    }
}
