using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProjectC.Api.Data;

// CLI schema generation requires no JWT key and never starts the HTTP application.
public sealed class GameDbContextFactory : IDesignTimeDbContextFactory<GameDbContext>
{
    public GameDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var connection = configuration.GetConnectionString("GameDatabase")
            ?? throw new InvalidOperationException("Set ConnectionStrings__GameDatabase before running dotnet ef.");
        return new(new DbContextOptionsBuilder<GameDbContext>().UseNpgsql(connection,
            p => p.MigrationsHistoryTable("__EFMigrationsHistory", "game")).Options);
    }
}
