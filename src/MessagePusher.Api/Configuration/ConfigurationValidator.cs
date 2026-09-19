namespace MessagePusher.Api.Configuration;

public static class ConfigurationValidator
{
    public static void ValidateOrThrow(IConfiguration configuration, IHostEnvironment env)
    {
        var provider = configuration["Database:Provider"];
        if (string.IsNullOrWhiteSpace(provider))
            throw new InvalidOperationException("关键配置缺失: Database:Provider");
        if (!provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase) &&
            !provider.Equals("mysql", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Database:Provider 必须是 sqlite 或 mysql");

        if (provider.Equals("mysql", StringComparison.OrdinalIgnoreCase))
        {
            var cs = configuration.GetConnectionString("Default") ?? configuration["SQL_DSN"];
            if (string.IsNullOrWhiteSpace(cs))
                throw new InvalidOperationException("关键配置缺失: ConnectionStrings:Default (MySQL)");
        }

        var secret = configuration["AppSettings:SessionSecret"] ?? configuration["SESSION_SECRET"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("关键配置缺失: AppSettings:SessionSecret / SESSION_SECRET");
    }
}
