using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Passless.Infrastructure;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Passless.IntegrationTests;

/// <summary>
/// Owns the real Postgres and Redis instances the suite runs against, and
/// applies the migrations to an empty database once per run.
/// </summary>
/// <remarks>
/// No in-memory provider and no mocked cache. The behaviour under test is
/// largely unique constraints, referential actions and a plpgsql trigger --
/// precisely the things a fake gets right by pretending. A test proving that
/// audit rows cannot be updated is worthless against a provider that has no
/// triggers to begin with.
/// </remarks>
public sealed class PasslessFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine")
        .Build();

    private PasslessApiFactory? _api;

    public PasslessApiFactory Api =>
        _api ?? throw new InvalidOperationException("Fixture has not been initialised.");

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public string RedisConnectionString => _redis.GetConnectionString();

    /// <summary>
    /// A second host over the same containers, with configuration overridden.
    /// Used by the expiry test, which needs a challenge TTL short enough to wait
    /// out. The caller owns the returned factory.
    /// </summary>
    public PasslessApiFactory CreateApi(IReadOnlyDictionary<string, string> settings) =>
        new(_postgres.GetConnectionString(), _redis.GetConnectionString(), settings);

    public async Task InitializeAsync()
    {
        // Started together: container startup dominates the wall-clock time of
        // the whole suite and these two have no ordering relationship.
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        _api = new PasslessApiFactory(_postgres.GetConnectionString(), _redis.GetConnectionString());

        // Migrating through the application's own service provider, rather than
        // building a context here, means the suite exercises the registration
        // the API actually ships. A context configured separately could differ
        // in naming convention and still pass every test.
        await using var scope = CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();
        await database.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }

    public AsyncServiceScope CreateScope() => Api.Services.CreateAsyncScope();
}

/// <summary>
/// Boots the shipping host with the throwaway containers wired in.
/// </summary>
public sealed class PasslessApiFactory(
    string postgresConnectionString,
    string redisConnectionString,
    IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    /// <summary>The single origin these tests are allowed to run ceremonies from.</summary>
    public const string Origin = "https://localhost:4200";

    public const string RelyingPartyId = "localhost";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" rather than "Development": the development branch loads a
        // local TLS certificate off disk, which does not exist in CI and is not
        // what these tests are about.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", postgresConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", redisConnectionString);

        builder.UseSetting("WebAuthn:RelyingPartyId", RelyingPartyId);
        builder.UseSetting("WebAuthn:RelyingPartyName", "Passless (tests)");
        builder.UseSetting("WebAuthn:Origins:0", Origin);
        builder.UseSetting("WebAuthn:ChallengeTimeToLive", "00:02:00");
        builder.UseSetting("Tokens:SigningKey", "cGFzc2xlc3MtaW50ZWdyYXRpb24tdGVzdHMtc2lnbmluZy1rZXktMzI=");
        builder.UseSetting("Tokens:Issuer", "https://localhost");
        builder.UseSetting("Tokens:Audience", "passless-tests");
        builder.UseSetting("Tokens:AccessTokenLifetime", "00:05:00");
        builder.UseSetting("Tokens:RefreshTokenLifetime", "30.00:00:00");
        builder.UseSetting("WebAuthn:DecoyKey", "cGFzc2xlc3MtaW50ZWdyYXRpb24tdGVzdC1kZWNveS1rZXktMzJi");

        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }
    }

    /// <summary>
    /// A client with no cookie container, on an https base address.
    ///
    /// Deliberately not the cookie-managing client. These tests replay
    /// ceremonies, submit the same handle twice at once, and send handles that
    /// were never issued; with a container in the chain a request can carry a
    /// stored cookie *in addition* to the one the test attached, and a test that
    /// means to send a bogus handle quietly sends a valid one too. Every request
    /// here carries exactly the cookie its test names, and nothing else.
    /// </summary>
    public HttpClient CreateCeremonyClient()
    {
        var client = Server.CreateClient();
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }
}

[CollectionDefinition(Name)]
public sealed class PasslessCollection : ICollectionFixture<PasslessFixture>
{
    public const string Name = "passless";
}
