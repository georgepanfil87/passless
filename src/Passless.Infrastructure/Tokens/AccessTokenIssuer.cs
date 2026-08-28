using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Passless.Infrastructure.Tokens;

internal sealed class AccessTokenIssuer(IOptions<TokenOptions> options)
{
    private readonly JsonWebTokenHandler _handler = new();

    /// <remarks>
    /// HS256 with a symmetric key, because one service both issues and verifies
    /// these. The moment a second service needs to verify without being able to
    /// mint, this has to become asymmetric with a published JWKS — a symmetric
    /// key shared with a verifier is a key that verifier can forge with.
    /// </remarks>
    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid userId, Guid sessionId, DateTimeOffset now)
    {
        var settings = options.Value;
        var expiresAt = now + settings.AccessTokenLifetime;

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = userId.ToString(),
                // The session this token was minted for. Revoking a device has to
                // be attributable to something inside the token, or a revoked
                // session's access tokens are indistinguishable from live ones.
                ["sid"] = sessionId.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(settings.SigningKeyBytes),
                SecurityAlgorithms.HmacSha256),
        });

        return (token, expiresAt);
    }
}
