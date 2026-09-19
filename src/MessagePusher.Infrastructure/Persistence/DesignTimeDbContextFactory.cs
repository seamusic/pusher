using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MessagePusher.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cfg = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", true)
            .AddEnvironmentVariables()
            .Build();
        var provider = cfg["Database:Provider"] ?? "sqlite";
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (string.Equals(provider, "mysql", StringComparison.OrdinalIgnoreCase))
        {
            var cs = cfg.GetConnectionString("Default") ?? "";
            builder.UseMySql(cs, ServerVersion.AutoDetect(cs));
        }
        else
        {
            var path = cfg["Database:SqlitePath"] ?? "message-pusher.db";
            builder.UseSqlite("Data Source=" + path);
        }
        return new AppDbContext(builder.Options);
    }
}
