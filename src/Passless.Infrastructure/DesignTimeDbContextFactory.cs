using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Passless.Infrastructure;

/// <summary>
/// Used only by `dotnet ef`. The connection string is never opened for
/// scaffolding, so a placeholder is correct here — a real one would be a
/// credential sitting in source for no reason.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PasslessDbContext>
{
    public PasslessDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("PASSLESS_DESIGN_TIME_CONNECTION")
            ?? "Host=localhost;Database=passless;Username=passless;Password=passless";

        var options = new DbContextOptionsBuilder<PasslessDbContext>()
            .UsePassless(connectionString)
            .Options;

        return new PasslessDbContext(options);
    }
}
