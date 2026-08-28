using Microsoft.Extensions.Options;

namespace Passless.Infrastructure.Tokens;

internal sealed class TokenOptionsValidator : IValidateOptions<TokenOptions>
{
    public ValidateOptionsResult Validate(string? name, TokenOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("Tokens:Issuer must be set.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Tokens:Audience must be set.");
        }

        if (options.AccessTokenLifetime <= TimeSpan.Zero)
        {
            failures.Add("Tokens:AccessTokenLifetime must be positive.");
        }

        if (options.RefreshTokenLifetime <= options.AccessTokenLifetime)
        {
            failures.Add("Tokens:RefreshTokenLifetime must exceed Tokens:AccessTokenLifetime.");
        }

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            failures.Add("Tokens:SigningKey must be set to a base64 value of at least 32 bytes.");
        }
        else
        {
            Span<byte> buffer = stackalloc byte[128];
            if (!Convert.TryFromBase64String(options.SigningKey, buffer, out var length))
            {
                failures.Add("Tokens:SigningKey is not valid base64.");
            }
            else if (length < 32)
            {
                // HS256 truncates nothing, but a key shorter than the digest it
                // produces gives less security than the algorithm advertises.
                failures.Add($"Tokens:SigningKey decodes to {length} bytes; HS256 requires at least 32.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
