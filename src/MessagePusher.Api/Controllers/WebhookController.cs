using MessagePusher.Api.Auth;
using MessagePusher.Application.Results;
using MessagePusher.Application.Services;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;

namespace MessagePusher.Api.Controllers;

[ApiController]
[Route("api/webhook")]
[RequireRole(Roles.Common)]
public sealed class WebhookController : ControllerBase
{
    private readonly WebhookService _webhooks;
    public WebhookController(WebhookService webhooks) => _webhooks = webhooks;

    [HttpGet("")]
    public async Task<IActionResult> List([FromQuery] int p, CancellationToken ct)
    {
        if (p < 0) p = 0;
        return Ok(ApiResult<object>.Ok(await _webhooks.ListAsync(CurrentUser.Id(HttpContext), p, ct)));
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string keyword, CancellationToken ct) =>
        Ok(ApiResult<object>.Ok(await _webhooks.SearchAsync(CurrentUser.Id(HttpContext), keyword ?? "", ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) =>
        Ok(ApiResult<object>.Ok(await _webhooks.GetAsync(id, CurrentUser.Id(HttpContext), ct)));

    [HttpPost("")]
    public async Task<IActionResult> Add([FromBody] Webhook webhook, CancellationToken ct)
    {
        await _webhooks.AddAsync(CurrentUser.Id(HttpContext), webhook, ct);
        return Ok(ApiResult.Ok());
    }

    [HttpPut("")]
    public async Task<IActionResult> Update([FromBody] Webhook webhook, [FromQuery] string? status_only, CancellationToken ct)
    {
        var data = await _webhooks.UpdateAsync(CurrentUser.Id(HttpContext), webhook, !string.IsNullOrEmpty(status_only), ct);
        return Ok(ApiResult<object>.Ok(data));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _webhooks.DeleteAsync(id, CurrentUser.Id(HttpContext), ct);
        return Ok(ApiResult.Ok());
    }
}
