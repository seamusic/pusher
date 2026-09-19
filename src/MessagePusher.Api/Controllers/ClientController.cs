using MessagePusher.Api.Middleware;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Application.Results;

namespace MessagePusher.Api.Controllers;

[ApiController]
public sealed class ClientController : ControllerBase
{
    [HttpGet("/api/register_client/{username}")]
    [CriticalRateLimit]
    public async Task Register(string username, [FromQuery] string secret, [FromQuery] string? channel,
        [FromServices] IUserRepository users, [FromServices] IChannelRepository channels,
        [FromServices] IWebSocketClientManager ws, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(secret))
        {
            Response.StatusCode = 200;
            await Response.WriteAsJsonAsync(ApiResult.Fail("secret 为空"));
            return;
        }
        var user = await users.GetByUsernameAsync(username, ct);
        if (user is null || user.Id == 0)
        {
            await Response.WriteAsJsonAsync(ApiResult.Fail("无效的用户名"));
            return;
        }
        var channelName = string.IsNullOrEmpty(channel) ? "client" : channel;
        var ch = await channels.GetByNameAsync(channelName, user.Id, ct);
        if (ch is null)
        {
            await Response.WriteAsJsonAsync(ApiResult.Fail("无效的通道名称"));
            return;
        }
        if (secret != ch.Secret)
        {
            await Response.WriteAsJsonAsync(ApiResult.Fail("通道名称与密钥不匹配"));
            return;
        }
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            await Response.WriteAsJsonAsync(ApiResult.Fail("需要 WebSocket 升级"));
            return;
        }
        var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await ws.RegisterAsync(channelName, user.Id, socket, ct);
    }
}
