using System.ComponentModel.DataAnnotations;

namespace Passless.Infrastructure.Tokens;

public sealed class TokenOptions
{
    public const string SectionName = "Tokens";

    /// <summary>Base64, at least 32 bytes. Validated at startup.</summary>
    [Required]
    public string SigningKey { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Short on purpose. An access token cannot be revoked before it expires,
    /// so its lifetime is the window in which a stolen one still works — that
    /// window is the price paid for not hitting the database on every request.
    /// </summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    private byte[]? _signingKeyBytes;

    public byte[] SigningKeyBytes => _signingKeyBytes ??= Convert.FromBase64String(SigningKey);
}
