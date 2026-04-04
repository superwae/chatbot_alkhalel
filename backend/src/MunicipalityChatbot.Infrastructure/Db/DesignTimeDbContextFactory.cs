using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MunicipalityChatbot.Infrastructure.Db;

// Needed for `dotnet ef` to generate migrations without running the API.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("POSTGRES__CONNECTION_STRING")
                   ?? "Host=localhost;Database=municipality_chatbot;Username=postgres;Password=postgres";

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn);

        return new AppDbContext(opts.Options);
    }
}

