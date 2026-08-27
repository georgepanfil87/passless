using System.Security.Cryptography.X509Certificates;
using Fido2NetLib;
using Microsoft.Extensions.Options;
using Passless.Api.Features.Authentication;
using Passless.Api.Features.Registration;
using Passless.Api.Features.WebAuthn;
using Passless.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

UseLocalCertificateIfPresent(builder);

builder.Services.AddPasslessInfrastructure(
    Required(builder.Configuration.GetConnectionString("Postgres"), "ConnectionStrings:Postgres"),
    Required(builder.Configuration.GetConnectionString("Redis"), "ConnectionStrings:Redis"));

builder.Services
    .AddOptions<WebAuthnOptions>()
    .Bind(builder.Configuration.GetSection(WebAuthnOptions.SectionName))
    // Validated at startup: a relying party misconfiguration produces
    // credentials that register successfully and can never be used again.
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<WebAuthnOptions>, WebAuthnOptionsValidator>();

builder.Services.AddSingleton<IFido2>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<WebAuthnOptions>>().Value;

    return new Fido2(
        new Fido2Configuration
        {
            ServerDomain = options.RelyingPartyId,
            ServerName = options.RelyingPartyName,
            // Stated, never inferred. Fido2NetLib will compare the ceremony
            // origin against exactly this set and nothing else.
            Origins = options.Origins.ToHashSet(StringComparer.Ordinal),
            ChallengeSize = 32,
        },
        // No metadata service: we record the AAGUID an authenticator claims but
        // do not check it against the FIDO Metadata Service, so we must not
        // present it as a verified fact anywhere.
        metadataService: null!);
});

builder.Services.AddScoped<RegistrationService>();
builder.Services.AddScoped<AuthenticationService>();

builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Liveness only, still. The database is now wired but nothing serves traffic
// from it yet, and a probe that reports "healthy" on the strength of a
// connection the application never uses is worse than no probe at all. The
// readiness checks land with the endpoints that depend on them.
app.MapHealthChecks("/health");
app.MapRegistration();
app.MapAuthentication();

app.Run();

static string Required(string? value, string key) =>
    !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException(
            $"{key} is not configured. Development values live in appsettings.Development.json; "
            + "every other environment supplies them out of band.");

// Serves the certificate produced by scripts/dev-certs.sh when one exists, so
// that the mkcert path (custom hostnames, real SANs) works without editing
// launch settings. With no file present Kestrel falls back to the SDK
// development certificate, which covers `localhost` and nothing else -- enough
// for WebAuthn, since `localhost` is a secure context on its own.
static void UseLocalCertificateIfPresent(WebApplicationBuilder builder)
{
    if (!builder.Environment.IsDevelopment())
    {
        return;
    }

    var certificatePath = builder.Configuration["DevelopmentCertificate:CertificatePath"];
    var keyPath = builder.Configuration["DevelopmentCertificate:KeyPath"];
    if (string.IsNullOrWhiteSpace(certificatePath) || string.IsNullOrWhiteSpace(keyPath))
    {
        return;
    }

    certificatePath = Path.GetFullPath(certificatePath, builder.Environment.ContentRootPath);
    keyPath = Path.GetFullPath(keyPath, builder.Environment.ContentRootPath);
    if (!File.Exists(certificatePath) || !File.Exists(keyPath))
    {
        return;
    }

    // Read from the PEM pair rather than a PKCS#12 bundle so that no passphrase
    // needs to exist anywhere. An unencrypted PFX is rejected outright by the
    // macOS Security framework, and an encrypted one would mean committing the
    // password that unlocks it -- the Angular dev server wants the PEM pair
    // regardless, so this is also one artefact instead of two.
    var certificate = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);

    if (OperatingSystem.IsWindows())
    {
        // SChannel cannot use the ephemeral private key CreateFromPemFile
        // produces; a round trip through PKCS#12 yields a key handle it accepts.
        using var ephemeral = certificate;
        certificate = new X509Certificate2(ephemeral.Export(X509ContentType.Pfx));
    }

    builder.WebHost.ConfigureKestrel(kestrel => kestrel.ConfigureHttpsDefaults(https =>
        https.ServerCertificate = certificate));
}

// Exposed so the integration tests can boot the real host through
// WebApplicationFactory<Program> instead of a re-declared test host that would
// drift away from what actually ships.
public partial class Program
{
}
