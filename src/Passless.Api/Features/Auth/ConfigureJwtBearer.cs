using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Passless.Core.Abstractions;
using Passless.Infrastructure.Tokens;

namespace Passless.Api.Features.Auth;

internal sealed class ConfigureJwtBearer(IOptions<TokenOptions> tokens)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        var settings = tokens.Value;

        // Keep the JWT's own claim names. The default mapping rewrites "sub"
        // into a WS-Federation URI, which makes every lookup in this codebase
        // disagree with the token a reader is holding.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,

            ValidateAudience = true,
            ValidAudience = settings.Audience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(settings.SigningKeyBytes),

            // Pinned. Without this a token could name its own algorithm, and the
            // "alg": "none" family of confusion attacks becomes available.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

            ValidateLifetime = true,

            // The default is five minutes, which would double the effective life
            // of a five-minute access token and double the revocation window
            // along with it. Thirty seconds covers ordinary clock drift.
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                // The signature was valid. That says the token was minted here
                // and has not expired; it says nothing about whether the session
                // behind it still exists.
                var sessionId = CurrentPrincipal.SessionId(context.Principal!);
                if (sessionId is null)
                {
                    context.Fail("Token carries no session.");
                    return;
                }

                var revocations = context.HttpContext.RequestServices
                    .GetRequiredService<ISessionRevocationCache>();

                if (await revocations.IsRevokedAsync(sessionId.Value, context.HttpContext.RequestAborted))
                {
                    context.Fail("Session revoked.");
                }
            },
        };
    }
}
