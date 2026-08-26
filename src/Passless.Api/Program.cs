using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);

UseLocalCertificateIfPresent(builder);

builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Liveness only, on purpose. A readiness probe that reports "healthy" without
// checking the database and Redis is worse than no probe at all, so the
// dependency checks land with the dependencies.
app.MapHealthChecks("/health");

app.Run();

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
