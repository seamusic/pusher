using MessagePusher.Application.Services;
using MessagePusher.Domain.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MessagePusher.Api.Controllers;

[ApiController]
[Route("push")]
public sealed class PushController : ControllerBase
{
    private readonly PushService _push;
    public PushController(PushService push) => _push = push;

    [HttpGet("{username}")]
    public async Task<IActionResult> Get(string username, CancellationToken ct)
    {
        var message = BindFromQuery();
        ApplyAuthToken(message);
        var result = await _push.PushAsync(username, message, true, ct);
        return Ok(result);
    }

    [HttpPost("{username}")]
    public async Task<IActionResult> Post(string username, CancellationToken ct)
    {
        Message message;
        var ctHeader = Request.ContentType ?? "";
        if (ctHeader.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            message = await Request.ReadFromJsonAsync<Message>(ct) ?? new Message();
            if (IsEmpty(message))
                return Ok(new { success = false, message = "请求体为空，如果使用 JSON 请设置 Content-Type 为 application/json，否则请使用表单提交" });
        }
        else
        {
            message = BindFromForm();
            if (IsEmpty(message))
                return Ok(new { success = false, message = "请求体为空，如果使用 JSON 请设置 Content-Type 为 application/json，否则请使用表单提交" });
        }
        if (string.IsNullOrEmpty(message.Token))
            message.Token = Request.Query["token"].ToString();
        ApplyAuthToken(message);
        var result = await _push.PushAsync(username, message, true, ct);
        return Ok(result);
    }

    private void ApplyAuthToken(Message message)
    {
        if (string.IsNullOrEmpty(message.Token))
        {
            var auth = Request.Headers.Authorization.ToString();
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                message.Token = auth["Bearer ".Length..].Trim();
        }
    }

    private Message BindFromQuery() => new()
    {
        Title = Request.Query["title"].ToString(),
        Description = Request.Query["description"].ToString(),
        Content = Request.Query["content"].ToString(),
        Url = Request.Query["url"].ToString(),
        Channel = Request.Query["channel"].ToString(),
        Token = Request.Query["token"].ToString(),
        To = Request.Query["to"].ToString(),
        Desp = Request.Query["desp"].ToString(),
        Short = Request.Query["short"].ToString(),
        OpenId = Request.Query["openid"].ToString(),
        Async = Request.Query["async"].ToString() == "true",
        RenderMode = Request.Query["render_mode"].ToString()
    };

    private Message BindFromForm() => new()
    {
        Title = Request.Form["title"].ToString(),
        Description = Request.Form["description"].ToString(),
        Content = Request.Form["content"].ToString(),
        Url = Request.Form["url"].ToString(),
        Channel = Request.Form["channel"].ToString(),
        Token = Request.Form["token"].ToString(),
        To = Request.Form["to"].ToString(),
        Desp = Request.Form["desp"].ToString(),
        Short = Request.Form["short"].ToString(),
        OpenId = Request.Form["openid"].ToString(),
        Async = Request.Form["async"].ToString() == "true",
        RenderMode = Request.Form["render_mode"].ToString()
    };

    private static bool IsEmpty(Message m) =>
        string.IsNullOrEmpty(m.Title) && string.IsNullOrEmpty(m.Description) && string.IsNullOrEmpty(m.Content)
        && string.IsNullOrEmpty(m.Url) && string.IsNullOrEmpty(m.Channel) && string.IsNullOrEmpty(m.Token)
        && string.IsNullOrEmpty(m.To) && string.IsNullOrEmpty(m.Desp) && string.IsNullOrEmpty(m.Short)
        && string.IsNullOrEmpty(m.OpenId);
}
