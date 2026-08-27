using Microsoft.Extensions.DependencyInjection;
using Passless.Core.Abstractions;
using Passless.Infrastructure.Auditing;
using Passless.Infrastructure.Challenges;
using StackExchange.Redis;

namespace Passless.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddPasslessInfrastructure(
        this IServiceCollection services,
        string postgresConnectionString,
        string redisConnectionString)
    {
        services.AddDbContext<PasslessDbContext>(options =>
            options.UsePassless(postgresConnectionString));

        services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(redisConnectionString));

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IChallengeStore, RedisChallengeStore>();
        services.AddScoped<IAuditLog, EfAuditLog>();

        return services;
    }
}
