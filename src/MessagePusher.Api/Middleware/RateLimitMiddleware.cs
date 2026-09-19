using MessagePusher.Application.Abstractions;
using MessagePusher.Domain;

namespace MessagePusher.Api.Middleware;

public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRateLimiter _limiter;
    private readonly string _mark;
    private readonly int _num;
    private readonly int _duration;
    private readonly Func<HttpContext, bool> _match;

    public RateLimitMiddleware(RequestDelegate next, IRateLimiter limiter, string mark, int num, int duration, Func<HttpContext, bool> match)
    {
        _next = next;
        _limiter = limiter;
        _mark = mark;
        _num = num;
        _duration = duration;
        _match = match;
    }

    public async Task Invoke(HttpContext context)
    {
        if (_match(context))
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!_limiter.Check(_mark + ip, _num, _duration))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return;
            }
        }
        await _next(context);
    }
}

public static class RateLimitMiddlewareExtensions
{
    public static IApplicationBuilder UseApiRateLimit(this IApplicationBuilder app) =>
        app.UseMiddleware<RateLimitMiddleware>(RateLimitDefaults.MarkApi, RateLimitDefaults.GlobalApiNum, RateLimitDefaults.GlobalApiDurationSeconds,
            (Func<HttpContext, bool>)(ctx =>
            {
                var p = ctx.Request.Path;
                return p.StartsWithSegments("/api") || p.StartsWithSegments("/push") || p.StartsWithSegments("/webhook");
            }));

    public static IApplicationBuilder UseWebRateLimit(this IApplicationBuilder app) =>
        app.UseMiddleware<RateLimitMiddleware>(RateLimitDefaults.MarkWeb, RateLimitDefaults.GlobalWebNum, RateLimitDefaults.GlobalWebDurationSeconds,
            (Func<HttpContext, bool>)(ctx =>
            {
                var p = ctx.Request.Path;
                return !p.StartsWithSegments("/api") && !p.StartsWithSegments("/push") && !p.StartsWithSegments("/webhook")
                       && !p.StartsWithSegments("/health") && !p.StartsWithSegments("/ready");
            }));
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class CriticalRateLimitAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var limiter = context.HttpContext.RequestServices.GetRequiredService<IRateLimiter>();
        var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!limiter.Check(RateLimitDefaults.MarkCritical + ip, RateLimitDefaults.CriticalNum, RateLimitDefaults.CriticalDurationSeconds))
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }
        await next();
    }
}
