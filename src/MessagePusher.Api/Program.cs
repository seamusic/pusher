using System.Text.Json;
using MessagePusher.Api.Configuration;
using MessagePusher.Api.Middleware;
using MessagePusher.Application;
using MessagePusher.Application.Json;
using MessagePusher.Infrastructure;
using MessagePusher.Infrastructure.Options;
using MessagePusher.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;
using Serilog;
using StackExchange.Redis;

var versionPrinted = args.Any(a => a is "--version" or "-version");
if (versionPrinted)
{
    Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0");
    return;
}
if (args.Any(a => a is "--help" or "-help" or "-h"))
{
    Console.WriteLine("Message Pusher - Your all in one message push system.");
    Console.WriteLine("Options:");
    Console.WriteLine("        --port           Specify the listening port. Default is 3000.");
    Console.WriteLine("        --log-dir        Specify the directory for log files.");
    Console.WriteLine("        --version        Print the version of the program and exits.");
    Console.WriteLine("        --help           Print the help message and exits.");
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
MapLegacyEnv(builder.Configuration);

var port = GetArg(args, "--port") ?? Environment.GetEnvironmentVariable("PORT") ?? builder.Configuration["Kestrel:Endpoints:Http:Url"] ?? "3000";
if (!port.StartsWith("http", StringComparison.OrdinalIgnoreCase))
    builder.WebHost.UseUrls("http://*:" + port.TrimStart(':'));

var logDir = GetArg(args, "--log-dir") ?? builder.Configuration["AppSettings:LogDir"];
var loggerConfig = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).Enrich.FromLogContext().WriteTo.Console();
if (!string.IsNullOrWhiteSpace(logDir))
{
    Directory.CreateDirectory(logDir);
    loggerConfig = loggerConfig.WriteTo.File(Path.Combine(logDir, "message-pusher-.log"), rollingInterval: RollingInterval.Day);
}
Log.Logger = loggerConfig.CreateLogger();
builder.Host.UseSerilog();

ConfigurationValidator.ValidateOrThrow(builder.Configuration, builder.Environment);

builder.Host.ConfigureHostOptions(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var keysPath = builder.Configuration["AppSettings:DataProtectionKeysPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "keys");
Directory.CreateDirectory(keysPath);
var dp = builder.Services.AddDataProtection().SetApplicationName("MessagePusher").PersistKeysToFileSystem(new DirectoryInfo(keysPath));
var redisCs = builder.Configuration.GetConnectionString("Redis") ?? builder.Configuration["REDIS_CONN_STRING"];
if (!string.IsNullOrWhiteSpace(redisCs))
    dp.PersistKeysToStackExchangeRedis(ConnectionMultiplexer.Connect(redisCs), "MessagePusher-Keys");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "session";
        o.Cookie.HttpOnly = true;
        o.Cookie.Path = "/";
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return ctx.Response.WriteAsJsonAsync(new { success = false, message = "无权进行此操作，未登录" }, JsonDefaults.Options);
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return ctx.Response.WriteAsJsonAsync(new { success = false, message = "无权进行此操作，权限不足" }, JsonDefaults.Options);
        };
    });
builder.Services.AddAuthorization();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSwaggerGen(o =>
    {
        o.SwaggerDoc("v1", new OpenApiInfo { Title = "Message Pusher API", Version = "v1" });
    });
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var init = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await init.InitializeAsync();
    var options = scope.ServiceProvider.GetRequiredService<SystemOptionService>();
    await options.InitAsync();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (ctx, next) =>
{
    var limiter = ctx.RequestServices.GetRequiredService<MessagePusher.Application.Abstractions.IRateLimiter>();
    var p = ctx.Request.Path;
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (p.StartsWithSegments("/api") || p.StartsWithSegments("/push") || p.StartsWithSegments("/webhook"))
    {
        if (!limiter.Check(MessagePusher.Domain.RateLimitDefaults.MarkApi + ip, MessagePusher.Domain.RateLimitDefaults.GlobalApiNum, MessagePusher.Domain.RateLimitDefaults.GlobalApiDurationSeconds))
        {
            ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }
    }
    else if (!p.StartsWithSegments("/health") && !p.StartsWithSegments("/ready") && !p.StartsWithSegments("/swagger"))
    {
        if (!limiter.Check(MessagePusher.Domain.RateLimitDefaults.MarkWeb + ip, MessagePusher.Domain.RateLimitDefaults.GlobalWebNum, MessagePusher.Domain.RateLimitDefaults.GlobalWebDurationSeconds))
        {
            ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }
    }
    await next();
});
app.UseWebSockets();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var name = ctx.File.Name;
        if (name.Contains('.') && (name.Contains("-") || name.Contains(".")))
        {
            var ext = Path.GetExtension(name);
            if (ext is ".js" or ".css" or ".woff" or ".woff2" or ".png" or ".jpg" or ".svg")
            {
                var fileName = Path.GetFileNameWithoutExtension(name);
                if (fileName.Any(char.IsDigit) && fileName.Contains('.'))
                    ctx.Context.Response.Headers.CacheControl = "public,max-age=604800";
            }
        }
    }
});
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapControllers();
app.MapFallbackToFile("index.html");

Log.Information("Message Pusher started");
app.Run();

public partial class Program
{
    private static string? GetArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == name && i + 1 < args.Length)
                return args[i + 1];
            if (args[i].StartsWith(name + "=", StringComparison.Ordinal))
                return args[i][(name.Length + 1)..];
        }
        return null;
    }

    private static void MapLegacyEnv(IConfigurationManager cfg)
    {
        var redis = Environment.GetEnvironmentVariable("REDIS_CONN_STRING");
        if (!string.IsNullOrEmpty(redis))
            cfg["ConnectionStrings:Redis"] = redis;
        var secret = Environment.GetEnvironmentVariable("SESSION_SECRET");
        if (!string.IsNullOrEmpty(secret))
            cfg["AppSettings:SessionSecret"] = secret;
        var dsn = Environment.GetEnvironmentVariable("SQL_DSN");
        if (!string.IsNullOrEmpty(dsn))
        {
            cfg["ConnectionStrings:Default"] = dsn;
            cfg["Database:Provider"] = "mysql";
        }
        var sqlite = Environment.GetEnvironmentVariable("SQLITE_PATH");
        if (!string.IsNullOrEmpty(sqlite))
            cfg["Database:SqlitePath"] = sqlite;
        var allow = Environment.GetEnvironmentVariable("CHANNEL_URL_ALLOW_NON_HTTPS");
        if (!string.IsNullOrEmpty(allow))
            cfg["AppSettings:ChannelUrlAllowNonHttps"] = allow;
    }
}
