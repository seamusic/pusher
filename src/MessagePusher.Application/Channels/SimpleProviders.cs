using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Json;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Channels;

public sealed class NoneProvider : IChannelProvider
{
    public string Type => ChannelType.None;
    public Task SendAsync(Message message, User user, Channel channel, CancellationToken ct) => Task.CompletedTask;
}

public sealed class EmailProvider : IChannelProvider
{
    private readonly IEmailSender _email;
    private readonly IMarkdownRenderer _md;
    private readonly ISystemOptionService _options;
    public string Type => ChannelType.Email;

    public EmailProvider(IEmailSender email, IMarkdownRenderer md, ISystemOptionService options)
    {
        _email = email;
        _md = md;
        _options = options;
    }

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        var email = user.Email;
        if (!string.IsNullOrEmpty(message.To))
        {
            if (user.SendEmailToOthers != UserPreference.Allowed && user.Role < Roles.Admin)
                throw new BusinessException("没有权限发送邮件给其他人，请联系管理员为你添加该权限");
            email = message.To;
        }
        if (string.IsNullOrEmpty(email))
            throw new BusinessException("未配置邮箱地址");

        var systemName = _options.Get("SystemName", AppDefaults.SystemName);
        var subject = message.Title;
        var content = message.Content;
        if (subject == systemName || string.IsNullOrEmpty(subject))
            subject = message.Description;
        else
            content = $"{message.Description}\n\n{message.Content}";

        message.HtmlContent = _md.ToHtml(content);
        email = email.Replace("|", ";");
        await _email.SendAsync(subject, email, message.HtmlContent ?? "", ct);
    }
}

public sealed class ClientProvider : IChannelProvider
{
    private readonly IWebSocketClientManager _ws;
    public string Type => ChannelType.Client;
    public ClientProvider(IWebSocketClientManager ws) => _ws = ws;
    public Task SendAsync(Message message, User user, Channel channel, CancellationToken ct) =>
        _ws.SendAsync(message, user, channel, ct);
}

public sealed class BarkProvider : IChannelProvider
{
    private readonly IHttpClientFactory _http;
    public string Type => ChannelType.Bark;
    public BarkProvider(IHttpClientFactory http) => _http = http;

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        var url = $"{channel.Url}/{channel.Secret}";
        var body = message.Content == "" ? message.Description : message.Content;
        var (resp, text) = await HttpChannelHelpers.PostRawAsync(_http.CreateClient("channels"), url,
            new { title = message.Title, body, url = message.Url }, ct);
        resp.Dispose();
        var res = JsonSerializer.Deserialize<BarkRes>(text, JsonDefaults.Options);
        if (res is null || res.Code != 200)
            throw new BusinessException(res?.Message ?? "bark failed");
    }

    private sealed class BarkRes { public int Code { get; set; } public string? Message { get; set; } }
}

public sealed class DiscordProvider : IChannelProvider
{
    private readonly IHttpClientFactory _http;
    public string Type => ChannelType.Discord;
    public DiscordProvider(IHttpClientFactory http) => _http = http;

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        if (message.Content == "")
            message.Content = message.Description;
        var content = message.Content;
        if (!string.IsNullOrEmpty(message.To))
        {
            var prefix = string.Concat(message.To.Split('|').Select(id => $"<@{id}> "));
            content = prefix + message.Content;
        }
        var client = _http.CreateClient("channels");
        var (resp, text) = await HttpChannelHelpers.PostRawAsync(client, channel.Url, new { content }, ct);
        using (resp)
        {
            if ((int)resp.StatusCode == 204)
                return;
            var res = string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<DiscordRes>(text, JsonDefaults.Options);
            if (res is { Code: not 0 })
                throw new BusinessException(res.Message ?? "discord failed");
            if ((int)resp.StatusCode == 400)
                throw new BusinessException(resp.ReasonPhrase ?? "Bad Request");
        }
    }

    private sealed class DiscordRes { public int Code { get; set; } public string? Message { get; set; } }
}

public sealed class CorpProvider : IChannelProvider
{
    private readonly IHttpClientFactory _http;
    public string Type => ChannelType.Corp;
    public CorpProvider(IHttpClientFactory http) => _http = http;

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        object body;
        if (message.Content == "")
            body = new { msgtype = "text", text = new { content = message.Description }, mentioned_list = SplitTo(message.To) };
        else
            body = new { msgtype = "markdown", markdown = new { content = message.Content }, mentioned_list = SplitTo(message.To) };
        var (_, text) = await HttpChannelHelpers.PostRawAsync(_http.CreateClient("channels"), channel.Url, body, ct);
        var res = JsonSerializer.Deserialize<ErrRes>(text, JsonDefaults.Options);
        if (res is null || res.Errcode != 0)
            throw new BusinessException(res?.Errmsg ?? "corp failed");
    }

    private static string[]? SplitTo(string to) => string.IsNullOrEmpty(to) ? null : to.Split('|');
    private sealed class ErrRes { public int Errcode { get; set; } public string? Errmsg { get; set; } }
}

public sealed class CustomProvider : IChannelProvider
{
    private readonly IHttpClientFactory _http;
    private readonly ISystemOptionService _options;
    public string Type => ChannelType.Custom;
    public CustomProvider(IHttpClientFactory http, ISystemOptionService options)
    {
        _http = http;
        _options = options;
    }

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        var url = channel.Url;
        var allowHttp = string.Equals(Environment.GetEnvironmentVariable("CHANNEL_URL_ALLOW_NON_HTTPS"), "true", StringComparison.OrdinalIgnoreCase)
                        || _options.GetBool("ChannelUrlAllowNonHttps");
        if (url.StartsWith("http:", StringComparison.OrdinalIgnoreCase) && !allowHttp)
            throw new BusinessException("自定义通道必须使用 HTTPS 协议");
        var server = _options.Get("ServerAddress", AppDefaults.ServerAddress);
        if (url.StartsWith(server, StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("自定义通道不能使用本服务地址");

        var template = channel.Other;
        template = QuoteReplace(template, "$url", message.Url);
        template = QuoteReplace(template, "$to", message.To);
        template = QuoteReplace(template, "$title", message.Title);
        template = QuoteReplace(template, "$description", message.Description);
        template = QuoteReplace(template, "$content", message.Content);
        var client = _http.CreateClient("channels");
        using var resp = await client.PostAsync(url, new StringContent(template, Encoding.UTF8, "application/json"), ct);
        if ((int)resp.StatusCode != 200)
            throw new BusinessException(resp.ReasonPhrase ?? resp.StatusCode.ToString());
    }

    private static string QuoteReplace(string s, string old, string value)
    {
        var quoted = JsonSerializer.Serialize(value);
        if (quoted.Length >= 2)
            quoted = quoted[1..^1];
        return s.Replace(old, quoted);
    }
}

public sealed class OneBotProvider : IChannelProvider
{
    private readonly IHttpClientFactory _http;
    public string Type => ChannelType.OneBot;
    public OneBotProvider(IHttpClientFactory http) => _http = http;

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        var url = $"{channel.Url}/send_msg";
        var text = message.Content == "" ? message.Description : message.Content;
        var target = string.IsNullOrEmpty(message.To) ? channel.AccountId : message.To;
        var parts = target.Split('_');
        string type;
        string idStr;
        if (parts.Length == 1) { type = "user"; idStr = parts[0]; }
        else if (parts.Length == 2) { type = parts[0]; idStr = parts[1]; }
        else throw new BusinessException("无效的 OneBot 配置");
        if (!long.TryParse(idStr, out var id))
            id = 0;
        object body = type switch
        {
            "user" => new { message_type = "private", user_id = id, group_id = 0L, message = text, auto_escape = false },
            "group" => new { message_type = "group", user_id = 0L, group_id = id, message = text, auto_escape = false },
            _ => throw new BusinessException("无效的 OneBot 配置")
        };
        var (resp, respText) = await HttpChannelHelpers.PostRawAsync(_http.CreateClient("channels"), url, body, ct,
            new Dictionary<string, string> { ["Authorization"] = "Bearer " + channel.Secret });
        using (resp)
        {
            if ((int)resp.StatusCode != 200)
                throw new BusinessException(resp.ReasonPhrase ?? resp.StatusCode.ToString());
            var res = JsonSerializer.Deserialize<OneBotRes>(respText, JsonDefaults.Options);
            if (res is null || res.Retcode != 0)
                throw new BusinessException(res?.Message ?? "one_bot failed");
        }
    }

    private sealed class OneBotRes { public string? Message { get; set; } public int Retcode { get; set; } }
}

public sealed class TencentAlarmProvider : IChannelProvider
{
    private readonly IHttpClientFactory _http;
    public string Type => ChannelType.TencentAlarm;
    public TencentAlarmProvider(IHttpClientFactory http) => _http = http;

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        var secretId = channel.AppId;
        var secretKey = channel.Secret;
        var policyId = channel.AccountId;
        var region = channel.Other;
        if (message.Description == "")
            message.Description = message.Content;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Random.Shared.Next(65535).ToString();
        var parameters = new Dictionary<string, string>
        {
            ["Action"] = "SendCustomAlarmMsg",
            ["Region"] = region,
            ["Timestamp"] = ts,
            ["Nonce"] = nonce,
            ["SecretId"] = secretId,
            ["policyId"] = policyId,
            ["msg"] = message.Description
        };
        var src = "GETmonitor.api.qcloud.com/v2/index.php?" +
                  string.Join("&", parameters.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "=" + k.Value));
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secretKey));
        var sign = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(src)));
        parameters["Signature"] = sign;
        var url = "https://monitor.api.qcloud.com/v2/index.php?" +
                  string.Join("&", parameters.Select(k => Uri.EscapeDataString(k.Key) + "=" + Uri.EscapeDataString(k.Value)));
        using var resp = await _http.CreateClient("channels").GetAsync(url, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        var res = JsonSerializer.Deserialize<TencentRes>(text, JsonDefaults.Options);
        if (res is null || res.Code != 0)
            throw new BusinessException(res?.Message ?? "tencent alarm failed");
    }

    private sealed class TencentRes { public int Code { get; set; } public string? Message { get; set; } }
}
