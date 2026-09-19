using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Infrastructure.Crypto;
using MessagePusher.Infrastructure.Email;
using MessagePusher.Infrastructure.Json;
using MessagePusher.Infrastructure.Markdown;
using MessagePusher.Infrastructure.Options;
using MessagePusher.Infrastructure.Persistence;
using MessagePusher.Infrastructure.Persistence.Repositories;
using MessagePusher.Infrastructure.RateLimit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace MessagePusher.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "sqlite";
        services.AddDbContext<AppDbContext>(options =>
        {
            if (string.Equals(provider, "mysql", StringComparison.OrdinalIgnoreCase))
            {
                var cs = configuration.GetConnectionString("Default")
                         ?? configuration["SQL_DSN"]
                         ?? throw new InvalidOperationException("MySQL connection string missing");
                options.UseMySql(cs, ServerVersion.AutoDetect(cs));
            }
            else
            {
                var path = configuration["Database:SqlitePath"]
                           ?? configuration["SQLITE_PATH"]
                           ?? "message-pusher.db";
                options.UseSqlite("Data Source=" + path);
            }
        });

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IChannelRepository, ChannelRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IOptionRepository, OptionRepository>();
        services.AddScoped<IWebhookRepository, WebhookRepository>();
        services.AddScoped<DatabaseInitializer>();

        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IGuidGenerator, GuidGenerator>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<AppCounters>();
        services.AddSingleton<IAppCounters>(sp => sp.GetRequiredService<AppCounters>());
        services.AddSingleton<IMarkdownRenderer, MarkdigRenderer>();
        services.AddSingleton<IEmailSender, SmtpMailSender>();
        services.AddSingleton<IGjson, GjsonPath>();
        services.AddSingleton<SystemOptionService>();
        services.AddSingleton<ISystemOptionService>(sp => sp.GetRequiredService<SystemOptionService>());

        var redis = configuration.GetConnectionString("Redis") ?? configuration["REDIS_CONN_STRING"];
        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));
            services.AddSingleton<IRateLimiter, RedisRateLimiter>();
            services.AddStackExchangeRedisCache(o => o.Configuration = redis);
        }
        else
        {
            services.AddSingleton<IRateLimiter, MemoryRateLimiter>();
            services.AddDistributedMemoryCache();
        }

        return services;
    }
}
