using MessagePusher.Api.Auth;
using MessagePusher.Application.Results;
using MessagePusher.Application.Services;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;

namespace MessagePusher.Api.Controllers;

[ApiController]
[Route("api/channel")]
[RequireRole(Roles.Common)]
public sealed class ChannelController : ControllerBase
{
    private readonly ChannelService _channels;
    public ChannelController(ChannelService channels) => _channels = channels;

    [HttpGet("")]
    public async Task<IActionResult> List([FromQuery] int p, [FromQuery] string? brief, CancellationToken ct)
    {
        var userId = CurrentUser.Id(HttpContext);
        if (!string.IsNullOrEmpty(brief))
            return Ok(ApiResult<object>.Ok(await _channels.BriefAsync(userId, ct)));
        if (p < 0) p = 0;
        return Ok(ApiResult<object>.Ok(await _channels.ListAsync(userId, p, ct)));
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string keyword, CancellationToken ct) =>
        Ok(ApiResult<object>.Ok(await _channels.SearchAsync(CurrentUser.Id(HttpContext), keyword ?? "", ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) =>
        Ok(ApiResult<object>.Ok(await _channels.GetAsync(id, CurrentUser.Id(HttpContext), ct)));

    [HttpPost("")]
    public async Task<IActionResult> Add([FromBody] Channel channel, CancellationToken ct)
    {
        await _channels.AddAsync(CurrentUser.Id(HttpContext), channel, ct);
        return Ok(ApiResult.Ok());
    }

    [HttpPut("")]
    public async Task<IActionResult> Update([FromBody] Channel channel, [FromQuery] string? status_only, CancellationToken ct)
    {
        var data = await _channels.UpdateAsync(CurrentUser.Id(HttpContext), channel, !string.IsNullOrEmpty(status_only), ct);
        return Ok(ApiResult<object>.Ok(data));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _channels.DeleteAsync(id, CurrentUser.Id(HttpContext), ct);
        return Ok(ApiResult.Ok());
    }
}
