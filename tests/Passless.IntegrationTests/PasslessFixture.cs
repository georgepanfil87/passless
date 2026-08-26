using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Passless.IntegrationTests;

/// <summary>
/// Owns the real Postgres and Redis instances the suite runs against.
/// </summary>
/// <remarks>
/// No in-memory provider and no mocked cache. The behaviour under test is
/// largely unique constraints, transaction isolation and atomic Redis
/// operations -- precisely the things a fake gets right by pretending. A test
/// that proves refresh-token reuse is detected is worthless if the store it
/// ran against could not have raced in the first place.
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

    public async Task InitializeAsync()
    {
        // Started together: container startup dominates the wall-clock time of
        // the whole suite and these two have no ordering relationship.
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        _api = new PasslessApiFactory(_postgres.GetConnectionString(), _redis.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }
}

/// <summary>
/// Boots the shipping host with the throwaway containers wired in.
/// </summary>
public sealed class PasslessApiFactory(string postgresConnectionString, string redisConnectionString)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" rather than "Development": the development branch loads a
        // local TLS certificate off disk, which does not exist in CI and is not
        // what these tests are about.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", postgresConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", redisConnectionString);
    }
}

[CollectionDefinition(Name)]
public sealed class PasslessCollection : ICollectionFixture<PasslessFixture>
{
    public const string Name = "passless";
}
