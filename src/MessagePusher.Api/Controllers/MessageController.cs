using MessagePusher.Api.Auth;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Results;
using MessagePusher.Application.Services;
using MessagePusher.Domain;

namespace MessagePusher.Api.Controllers;

[ApiController]
[Route("api/message")]
public sealed class MessageController : ControllerBase
{
    private readonly MessageService _messages;
    private readonly ISseBroker _sse;
    public MessageController(MessageService messages, ISseBroker sse)
    {
        _messages = messages;
        _sse = sse;
    }

    [HttpGet("")]
    [RequireRole(Roles.Common)]
    public async Task<IActionResult> List([FromQuery] int p, CancellationToken ct)
    {
        if (p < 0) p = 0;
        return Ok(ApiResult<object>.Ok(await _messages.ListAsync(CurrentUser.Id(HttpContext), p, ct)));
    }

    [HttpGet("search")]
    [RequireRole(Roles.Common)]
    public async Task<IActionResult> Search([FromQuery] string keyword, CancellationToken ct) =>
        Ok(ApiResult<object>.Ok(await _messages.SearchAsync(CurrentUser.Id(HttpContext), keyword ?? "", ct)));

    [HttpGet("status/{link}")]
    public async Task<IActionResult> Status(string link, CancellationToken ct)
    {
        try
        {
            var status = await _messages.GetStatusByLinkAsync(link, ct);
            return Ok(MessageStatusResult.Ok(status));
        }
        catch (Exception ex)
        {
            return Ok(MessageStatusResult.Fail(ex.Message));
        }
    }

    [HttpPost("resend/{id:int}")]
    [RequireRole(Roles.Common)]
    public async Task<IActionResult> Resend(int id, CancellationToken ct)
    {
        await _messages.ResendAsync(id, CurrentUser.Id(HttpContext), ct);
        return Ok(ApiResult.Ok());
    }

    [HttpGet("{id:int}")]
    [RequireRole(Roles.Common)]
    public async Task<IActionResult> Get(int id, CancellationToken ct) =>
        Ok(ApiResult<object>.Ok(await _messages.GetAsync(id, CurrentUser.Id(HttpContext), ct)));

    [HttpDelete("{id:int}")]
    [RequireRole(Roles.Common)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _messages.DeleteAsync(id, CurrentUser.Id(HttpContext), ct);
        return Ok(ApiResult.Ok());
    }

    [HttpDelete("")]
    [RequireRole(Roles.Root)]
    public async Task<IActionResult> DeleteAll(CancellationToken ct)
    {
        await _messages.DeleteAllAsync(ct);
        return Ok(ApiResult.Ok());
    }

    [HttpGet("stream")]
    [RequireRole(Roles.Common)]
    public async Task Stream(CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        await _sse.SubscribeAsync(CurrentUser.Id(HttpContext), Response.Body, ct);
    }
}
