namespace Passless.Core.Sessions;

/// <summary>
/// Turns a user agent string into something a person can recognise in a list of
/// their own devices — "Chrome 141 on macOS".
/// </summary>
/// <remarks>
/// Deliberately small, and deliberately not a library. Full user-agent parsing
/// needs a regularly updated regex database, which is a large dependency to
/// carry for a display string; and the string itself is a self-reported value
/// that no security decision depends on. The raw header is stored alongside
/// this, so nothing is lost when the guess is wrong.
///
/// It will be wrong sometimes. Every browser lies about being every other
/// browser for historical reasons, which is why the checks below run in a
/// specific order: Edge and Opera both claim to be Chrome, and Chrome claims to
/// be Safari. Swap in UAParser if the labels ever need to be dependable.
/// </remarks>
public static class DeviceLabel
{
    public const string Unknown = "Unknown device";

    public static string FromUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return Unknown;
        }

        var browser = Browser(userAgent);
        var platform = Platform(userAgent);

        return (browser, platform) switch
        {
            (null, null) => Unknown,
            (not null, null) => browser,
            (null, not null) => platform,
            _ => $"{browser} on {platform}",
        };
    }

    // Order matters: the more specific claim has to be tested first, because
    // every one of these agents also contains the tokens of the ones below it.
    private static string? Browser(string userAgent) =>
        Named(userAgent, "Edg/", "Edge")
        ?? Named(userAgent, "OPR/", "Opera")
        ?? Named(userAgent, "Firefox/", "Firefox")
        ?? Named(userAgent, "Chrome/", "Chrome")
        ?? Named(userAgent, "Version/", "Safari", requires: "Safari")
        ?? (userAgent.Contains("Safari", StringComparison.OrdinalIgnoreCase) ? "Safari" : null);

    private static string? Platform(string userAgent)
    {
        if (userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)) return "iPhone";
        if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase)) return "iPad";
        if (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase)) return "Android";
        if (userAgent.Contains("CrOS", StringComparison.Ordinal)) return "ChromeOS";
        if (userAgent.Contains("Windows NT", StringComparison.OrdinalIgnoreCase)) return "Windows";
        if (userAgent.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase)) return "macOS";
        if (userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase)) return "Linux";
        return null;
    }

    private static string? Named(string userAgent, string token, string name, string? requires = null)
    {
        if (requires is not null && !userAgent.Contains(requires, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var start = userAgent.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        var version = userAgent.AsSpan(start + token.Length);
        var length = 0;
        while (length < version.Length && char.IsAsciiDigit(version[length]))
        {
            length++;
        }

        // Major version only. The full quad changes weekly and tells the reader
        // nothing they wanted to know.
        return length == 0 ? name : $"{name} {version[..length]}";
    }
}
