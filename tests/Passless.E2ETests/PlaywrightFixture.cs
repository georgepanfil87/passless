using Microsoft.Playwright;

namespace Passless.E2ETests;

/// <summary>
/// Owns the browser the end-to-end suite drives.
/// </summary>
/// <remarks>
/// Chromium only, and not for convenience: the WebAuthn virtual authenticator
/// is a Chrome DevTools Protocol domain. Firefox and WebKit have no equivalent,
/// so a passkey ceremony cannot be exercised there without a physical
/// authenticator and a human finger.
/// </remarks>
public sealed class PlaywrightFixture : IAsyncLifetime
{
    /// <summary>Origin the suite drives; overridden in CI.</summary>
    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("PASSLESS_E2E_BASE_URL") ?? "https://localhost:4200";

    private static bool Headed =>
        Environment.GetEnvironmentVariable("PASSLESS_E2E_HEADED") == "1";

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    private IBrowser Browser =>
        _browser ?? throw new InvalidOperationException("Fixture has not been initialised.");

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = !Headed,
        });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
    }

    /// <summary>
    /// A fresh, isolated browser context -- no shared cookies or storage
    /// between tests, so a leaked session cannot make a later test pass.
    /// </summary>
    public Task<IBrowserContext> NewContextAsync() =>
        Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            // The local certificate is issued by a CA that exists only on the
            // developer's machine, and CI has no trust store at all. Accepted
            // here because these tests assert application behaviour, never the
            // PKI; the ceremonies still run in a secure context, which is what
            // WebAuthn actually requires.
            IgnoreHTTPSErrors = true,
        });
}

[CollectionDefinition(Name)]
public sealed class E2ECollection : ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "e2e";
}
