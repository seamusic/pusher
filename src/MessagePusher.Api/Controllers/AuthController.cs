using MessagePusher.Api.Auth;
using MessagePusher.Api.Middleware;
using MessagePusher.Application.Results;
using MessagePusher.Application.Services;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MessagePusher.Api.Controllers;

[ApiController]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    public AuthController(AuthService auth) => _auth = auth;

    [HttpPost("/api/user/login")]
    [CriticalRateLimit]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (req is null)
            return Ok(ApiResult.Fail("无效的参数"));
        var user = await _auth.LoginAsync(req.Username, req.Password, ct);
        await SignInAsync(user);
        return Ok(ApiResult<object>.Ok(Clean(user)));
    }

    [HttpGet("/api/user/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(ApiResult.Ok());
    }

    [HttpPost("/api/user/register")]
    [CriticalRateLimit]
    [TurnstileCheck]
    public async Task<IActionResult> Register([FromBody] User user, CancellationToken ct)
    {
        await _auth.RegisterAsync(user, ct);
        return Ok(ApiResult.Ok());
    }

    [HttpPost("/api/user/reset")]
    [CriticalRateLimit]
    public async Task<IActionResult> Reset([FromBody] ResetRequest req, CancellationToken ct)
    {
        var password = await _auth.ResetPasswordAsync(req.Email, req.Token, ct);
        return Ok(ApiResult<string>.Ok(password));
    }

    [HttpGet("/api/oauth/github")]
    [CriticalRateLimit]
    public async Task<IActionResult> GitHub([FromQuery] string code, CancellationToken ct)
    {
        var sessionId = User.Identity?.IsAuthenticated == true ? CurrentUser.Id(HttpContext) : (int?)null;
        if (sessionId is 0)
            sessionId = null;
        try
        {
            var user = await _auth.GitHubOAuthAsync(code, sessionId, ct);
            await SignInAsync(user);
            return Ok(ApiResult<object>.Ok(Clean(user)));
        }
        catch (BindCompleteException)
        {
            return Ok(new ApiResult { IsSuccess = true, Message = "bind" });
        }
    }

    [HttpGet("/api/oauth/wechat")]
    [CriticalRateLimit]
    public async Task<IActionResult> WeChat([FromQuery] string code, CancellationToken ct)
    {
        var user = await _auth.WeChatAuthAsync(code, ct);
        await SignInAsync(user);
        return Ok(ApiResult<object>.Ok(Clean(user)));
    }

    [HttpGet("/api/oauth/wechat/bind")]
    [CriticalRateLimit]
    [RequireRole(Roles.Common)]
    public async Task<IActionResult> WeChatBind([FromQuery] string code, CancellationToken ct)
    {
        await _auth.WeChatBindAsync(CurrentUser.Id(HttpContext), code, ct);
        return Ok(ApiResult.Ok());
    }

    [HttpGet("/api/oauth/email/bind")]
    [CriticalRateLimit]
    [RequireRole(Roles.Common)]
    public async Task<IActionResult> EmailBind([FromQuery] string email, [FromQuery] string code, CancellationToken ct)
    {
        await _auth.BindEmailAsync(CurrentUser.Id(HttpContext), email, code, ct);
        return Ok(ApiResult.Ok());
    }

    private async Task SignInAsync(User user)
    {
        var claims = new List<Claim>
        {
            new(AuthClaims.Id, user.Id.ToString()),
            new(AuthClaims.Username, user.Username),
            new(AuthClaims.Role, user.Role.ToString()),
            new(AuthClaims.Status, user.Status.ToString())
        };
        var id = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });
    }

    private static object Clean(User user) => new
    {
        id = user.Id,
        username = user.Username,
        display_name = user.DisplayName,
        role = user.Role,
        status = user.Status
    };

    public sealed class LoginRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public sealed class ResetRequest
    {
        public string Email { get; set; } = "";
        public string Token { get; set; } = "";
    }
}
