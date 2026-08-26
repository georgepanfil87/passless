using Microsoft.EntityFrameworkCore;

namespace Passless.Infrastructure;

public static class PasslessDbContextOptionsExtensions
{
    /// <summary>
    /// The single place the provider is configured.
    ///
    /// The API, the design-time migration factory and the integration tests all
    /// go through here. Configuring them separately is how a model gets built
    /// with one naming convention and migrated with another — a mismatch that
    /// only shows up as a column that mysteriously does not exist.
    /// </summary>
    public static DbContextOptionsBuilder UsePassless(
        this DbContextOptionsBuilder builder,
        string connectionString)
    {
        return builder
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(PasslessDbContext).Assembly.GetName().Name))
            .UseSnakeCaseNamingConvention();
    }

    /// <summary>Typed overload, so callers keep <c>DbContextOptions&lt;T&gt;</c>.</summary>
    public static DbContextOptionsBuilder<TContext> UsePassless<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        string connectionString)
        where TContext : DbContext
    {
        UsePassless((DbContextOptionsBuilder)builder, connectionString);
        return builder;
    }
}
