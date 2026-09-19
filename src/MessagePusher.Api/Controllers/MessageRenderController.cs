using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Services;

namespace MessagePusher.Api.Controllers;

[ApiController]
public sealed class MessageRenderController : ControllerBase
{
    private readonly MessageService _messages;
    private readonly ISystemOptionService _options;
    private readonly IMarkdownRenderer _md;

    public MessageRenderController(MessageService messages, ISystemOptionService options, IMarkdownRenderer md)
    {
        _messages = messages;
        _options = options;
        _md = md;
    }

    [HttpGet("/message/{link}")]
    public async Task<IActionResult> Render(string link, CancellationToken ct)
    {
        var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "message.html");
        var tpl = await System.IO.File.ReadAllTextAsync(templatePath, ct);
        if (!_options.GetBool("MessageRenderEnabled", true))
            return Content(Fill(tpl, "无法渲染", DateTime.Now, "超级管理员禁用了消息渲染",
                "很抱歉，您所使用的消息推送服务的管理员禁用了消息渲染功能，因此您的消息无法渲染。"), "text/html; charset=utf-8");
        if (link == "unsaved")
            return Content(Fill(tpl, "无法渲染", DateTime.Now, "超级管理员禁用了消息持久化",
                "很抱歉，您所使用的消息推送服务的管理员禁用了消息持久化功能，您的消息并没有存储到数据库中，因此无法渲染。"), "text/html; charset=utf-8");

        var message = await _messages.GetByLinkAsync(link, ct);
        if (message is null)
            return NotFound();

        var description = message.Description;
        var content = message.Content;
        if (message.RenderMode != "raw")
        {
            if (!string.IsNullOrEmpty(description))
                description = _md.ToHtml(description);
            if (!string.IsNullOrEmpty(content))
                content = _md.ToHtml(content);
        }
        var time = DateTimeOffset.FromUnixTimeSeconds(message.Timestamp).LocalDateTime;
        return Content(Fill(tpl, message.Title, time, description, content), "text/html; charset=utf-8");
    }

    private static string Fill(string tpl, string title, DateTime time, string description, string content)
    {
        return tpl
            .Replace("{{title}}", System.Net.WebUtility.HtmlEncode(title))
            .Replace("{{time}}", time.ToString("yyyy-MM-dd HH:mm:ss"))
            .Replace("{{description}}", description)
            .Replace("{{content}}", content);
    }
}
