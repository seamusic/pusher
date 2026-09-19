using MessagePusher.Api.Middleware;
using MessagePusher.Application.Results;
using MessagePusher.Application.Services;

namespace MessagePusher.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class MiscController : ControllerBase
{
    private readonly MiscService _misc;
    private readonly AuthService _auth;
    private readonly IHostEnvironment _env;

    public MiscController(MiscService misc, AuthService auth, IHostEnvironment env)
    {
        _misc = misc;
        _auth = auth;
        _env = env;
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        return Ok(ApiResult<object>.Ok(_misc.GetStatus(version)));
    }

    [HttpGet("notice")]
    public IActionResult Notice() => Ok(ApiResult<string>.Ok(_misc.GetNotice()));

    [HttpGet("about")]
    public IActionResult About() => Ok(ApiResult<string>.Ok(_misc.GetAbout()));

    [HttpGet("verification")]
    [CriticalRateLimit]
    [TurnstileCheck]
    public async Task<IActionResult> Verification([FromQuery] string email, CancellationToken ct)
    {
        await _auth.SendEmailVerificationAsync(email, ct);
        return Ok(ApiResult.Ok());
    }

    [HttpGet("reset_password")]
    [CriticalRateLimit]
    [TurnstileCheck]
    public async Task<IActionResult> ResetPasswordEmail([FromQuery] string email, CancellationToken ct)
    {
        await _auth.SendPasswordResetEmailAsync(email, ct);
        return Ok(ApiResult.Ok());
    }
}
