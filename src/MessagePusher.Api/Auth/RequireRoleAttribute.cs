using System.Security.Claims;
using MessagePusher.Application.Results;
using MessagePusher.Domain;

namespace MessagePusher.Api.Auth;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireRoleAttribute : Attribute, IAsyncActionFilter
{
    private readonly int _minRole;
    public RequireRoleAttribute(int minRole) => _minRole = minRole;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var username = user.FindFirstValue(AuthClaims.Username) ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(username))
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.HttpContext.Response.WriteAsJsonAsync(ApiResult.Fail("无权进行此操作，未登录"));
            return;
        }

        var statusStr = user.FindFirstValue(AuthClaims.Status);
        if (!int.TryParse(statusStr, out var status))
            status = (int)UserStatus.Enabled;
        if (status == (int)UserStatus.Disabled)
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
            await context.HttpContext.Response.WriteAsJsonAsync(ApiResult.Fail("用户已被封禁"));
            return;
        }

        var roleStr = user.FindFirstValue(AuthClaims.Role);
        if (!int.TryParse(roleStr, out var role))
            role = 0;
        if (role < _minRole)
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
            await context.HttpContext.Response.WriteAsJsonAsync(ApiResult.Fail("无权进行此操作，权限不足"));
            return;
        }

        await next();
    }
}

public static class CurrentUser
{
    public static int Id(HttpContext ctx) => int.TryParse(ctx.User.FindFirstValue(AuthClaims.Id), out var id) ? id : 0;
    public static int Role(HttpContext ctx) => int.TryParse(ctx.User.FindFirstValue(AuthClaims.Role), out var role) ? role : 0;
    public static string? Username(HttpContext ctx) => ctx.User.FindFirstValue(AuthClaims.Username);
}
