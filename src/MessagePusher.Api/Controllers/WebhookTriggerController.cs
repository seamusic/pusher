using MessagePusher.Application.Services;

namespace MessagePusher.Api.Controllers;

[ApiController]
[Route("webhook")]
public sealed class WebhookTriggerController : ControllerBase
{
    private readonly WebhookService _webhooks;
    public WebhookTriggerController(WebhookService webhooks) => _webhooks = webhooks;

    [HttpPost("{link}")]
    public async Task<IActionResult> Trigger(string link, CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        var uuid = await _webhooks.TriggerAsync(link, body, ct);
        return Ok(new { success = true, message = "", uuid });
    }
}
