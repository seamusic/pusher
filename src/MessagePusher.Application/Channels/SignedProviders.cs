using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Json;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace MessagePusher.Application.Channels;

public sealed class DingProvider : IChannelProvider
{
    private readonly IHttpClientFactory _http;
    public string Type => ChannelType.Ding;
    public DingProvider(IHttpClientFactory http) => _http = http;

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        object body;
        if (message.Content == "")
            body = new { msgtype = "text", text = new { content = message.Description }, at = BuildAt(message.To) };
        else
            body = new { msgtype = "markdown", markdown = new { title = message.Title, text = message.Content }, at = BuildAt(message.To) };

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var sign = DingSign(channel.Secret, timestamp);
        var url = $"{channel.Url}&timestamp={timestamp}&sign={sign}";
        var (_, text) = await HttpChannelHelpers.PostRawAsync(_http.CreateClient("channels"), url, body, ct);
        var res = JsonSerializer.Deserialize<DingRes>(text, JsonDefaults.Options);
        if (res is null || res.Errcode != 0)
            throw new BusinessException(res?.Errmsg ?? "ding failed");
    }

    private static object BuildAt(string to)
    {
        if (string.IsNullOrEmpty(to))
            return new { atUserIds = Array.Empty<string>(), isAtAll = false };
        if (to == "@all")
            return new { atUserIds = Array.Empty<string>(), isAtAll = true };
        return new { atUserIds = to.Split('|'), isAtAll = false };
    }

    public static string DingSign(string secret, long timestamp)
    {
        var stringToSign = $"{timestamp}\n{secret}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
        return Uri.EscapeDataString(signature);
    }

    private sealed class DingRes { public int Errcode { get; set; } public string? Errmsg { get; set; } }
}

public sealed class LarkProvider : IChannelProvider
{
    private readonly IHttpClientFactory _http;
    public string Type => ChannelType.Lark;
    public LarkProvider(IHttpClientFactory http) => _http = http;

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        var atPrefix = LarkAt.Prefix(message);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sign = LarkSign(channel.Secret, timestamp);
        object body;
        if (message.Content == "")
        {
            body = new
            {
                msg_type = "text",
                timestamp = timestamp.ToString(),
                sign,
                content = new { text = atPrefix + message.Description }
            };
        }
        else
        {
            body = new
            {
                msg_type = "interactive",
                timestamp = timestamp.ToString(),
                sign,
                card = new
                {
                    config = new { wide_screen_mode = true, enable_forward = true },
                    elements = new[]
                    {
                        new { tag = "div", text = new { content = atPrefix + message.Content, tag = "lark_md" } }
                    }
                }
            };
        }
        var (_, text) = await HttpChannelHelpers.PostRawAsync(_http.CreateClient("channels"), channel.Url, body, ct);
        var res = JsonSerializer.Deserialize<LarkRes>(text, JsonDefaults.Options);
        if (res is null || res.Code != 0)
            throw new BusinessException(res?.Msg ?? "lark failed");
    }

    public static string LarkSign(string secret, long timestamp)
    {
        var stringToSign = $"{timestamp}\n{secret}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(stringToSign));
        return Convert.ToBase64String(hmac.ComputeHash(Array.Empty<byte>()));
    }

    private sealed class LarkRes { public int Code { get; set; } public string? Msg { get; set; } }
}

internal static class LarkAt
{
    public static string Prefix(Message message)
    {
        if (string.IsNullOrEmpty(message.To))
            return "";
        if (message.To == "@all")
            return "<at user_id=\"all\">所有人</at>";
        return string.Concat(message.To.Split('|').Select(id => $"<at user_id=\"{id}\"> </at>"));
    }
}

public sealed class LarkAppProvider : IChannelProvider
{
    private readonly IHttpClientFactory _http;
    private readonly ITokenStore _tokens;
    public string Type => ChannelType.LarkApp;
    public LarkAppProvider(IHttpClientFactory http, ITokenStore tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        var rawTarget = string.IsNullOrEmpty(message.To) ? channel.AccountId : message.To;
        var parts = rawTarget.Split(':');
        if (parts.Length != 2)
            throw new BusinessException("无效的飞书应用号消息接收者参数");
        var targetType = parts[0];
        var target = parts[1];
        var atPrefix = LarkAt.Prefix(message);
        string msgType;
        string contentJson;
        if (!string.IsNullOrEmpty(message.Description))
        {
            msgType = "text";
            contentJson = JsonSerializer.Serialize(new { text = atPrefix + message.Description }, JsonDefaults.Options);
        }
        else
        {
            msgType = "interactive";
            contentJson = JsonSerializer.Serialize(new
            {
                config = new { wide_screen_mode = true, enable_forward = true },
                elements = new[]
                {
                    new { tag = "div", text = new { content = atPrefix + message.Content, tag = "lark_md" } }
                }
            }, JsonDefaults.Options);
        }
        var body = new { receive_id = target, msg_type = msgType, content = contentJson };
        var token = _tokens.GetToken(channel.AppId + channel.Secret);
        var url = $"https://open.feishu.cn/open-apis/im/v1/messages?receive_id_type={targetType}";
        var (resp, text) = await HttpChannelHelpers.PostRawAsync(_http.CreateClient("channels"), url, body, ct,
            new Dictionary<string, string> { ["Authorization"] = "Bearer " + token });
        resp.Dispose();
        var res = JsonSerializer.Deserialize<LarkRes>(text, JsonDefaults.Options);
        if (res is null || res.Code != 0)
            throw new BusinessException(res?.Msg ?? "lark_app failed");
    }

    private sealed class LarkRes { public int Code { get; set; } public string? Msg { get; set; } }
}

public sealed class TelegramProvider : IChannelProvider
{
    private readonly IHttpClientFactory _http;
    public string Type => ChannelType.Telegram;
    public const int MaxLength = 4096;
    public TelegramProvider(IHttpClientFactory http) => _http = http;

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        var chatId = string.IsNullOrEmpty(message.To) ? channel.AccountId : message.To;
        var parseMode = "";
        var text = message.Description;
        if (message.Content != "")
        {
            text = message.Content;
            parseMode = "markdown";
        }
        var idx = 0;
        while (idx < text.Length)
        {
            var nextIdx = idx + MaxLength;
            if (nextIdx > text.Length)
                nextIdx = text.Length;
            else
                nextIdx = GetNearestValidSplit(text, nextIdx, parseMode);
            var slice = text[idx..nextIdx];
            idx = nextIdx;
            var body = new { chat_id = chatId, text = slice, parse_mode = parseMode };
            var url = $"https://api.telegram.org/bot{channel.Secret}/sendMessage";
            var (_, respText) = await HttpChannelHelpers.PostRawAsync(_http.CreateClient("channels"), url, body, ct);
            var res = JsonSerializer.Deserialize<TgRes>(respText, JsonDefaults.Options);
            if (res is null || !res.Ok)
                throw new BusinessException(res?.Description ?? "telegram failed");
        }
    }

    public static int GetNearestValidSplit(string s, int idx, string mode) =>
        mode == "markdown" ? GetMarkdownSplit(s, idx) : GetPlainSplit(s, idx);

    private static int GetPlainSplit(string s, int idx)
    {
        if (idx >= s.Length) return idx;
        if (idx == 0) return 0;
        return char.IsLowSurrogate(s[idx]) ? GetPlainSplit(s, idx - 1) : idx;
    }

    private static int GetMarkdownSplit(string s, int idx)
    {
        if (idx >= s.Length) return idx;
        if (idx == 0) return 0;
        for (var i = idx; i >= 0; i--)
        {
            if (s[i] == '\n')
                return i + 1;
        }
        return idx;
    }

    private sealed class TgRes { public bool Ok { get; set; } public string? Description { get; set; } }
}

public sealed class WeChatTestProvider : IChannelProvider
{
    private readonly IHttpClientFactory _http;
    private readonly ITokenStore _tokens;
    public string Type => ChannelType.WeChatTestAccount;
    public WeChatTestProvider(IHttpClientFactory http, ITokenStore tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        var toUser = string.IsNullOrEmpty(message.To) ? channel.AccountId : message.To;
        var body = new
        {
            touser = toUser,
            template_id = channel.Other,
            url = message.Url,
            data = new
            {
                text = new { value = message.Description },
                title = new { value = message.Title },
                description = new { value = message.Description },
                content = new { value = message.Content }
            }
        };
        var token = _tokens.GetToken(channel.AppId + channel.Secret);
        var url = $"https://api.weixin.qq.com/cgi-bin/message/template/send?access_token={token}";
        var (_, text) = await HttpChannelHelpers.PostRawAsync(_http.CreateClient("channels"), url, body, ct);
        var res = JsonSerializer.Deserialize<WxRes>(text, JsonDefaults.Options);
        if (res is null || res.Errcode != 0)
            throw new BusinessException(res?.Errmsg ?? "wechat test failed");
    }

    private sealed class WxRes { public int Errcode { get; set; } public string? Errmsg { get; set; } }
}

public sealed class WeChatCorpProvider : IChannelProvider
{
    private readonly IHttpClientFactory _http;
    private readonly ITokenStore _tokens;
    private readonly ISystemOptionService _options;
    public string Type => ChannelType.WeChatCorpAccount;
    public WeChatCorpProvider(IHttpClientFactory http, ITokenStore tokens, ISystemOptionService options)
    {
        _http = http;
        _tokens = tokens;
        _options = options;
    }

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        var parts = channel.AppId.Split('|');
        if (parts.Length != 2)
            throw new BusinessException("无效的微信企业号配置");
        var corpId = parts[0];
        var agentId = parts[1];
        var toUser = string.IsNullOrEmpty(message.To) ? channel.AccountId : message.To;
        var clientType = channel.Other;
        object body;
        if (message.Content == "")
        {
            if (message.Title == "")
                body = new { msgtype = "text", touser = toUser, agentid = agentId, text = new { content = message.Description } };
            else
                body = new
                {
                    msgtype = "textcard",
                    touser = toUser,
                    agentid = agentId,
                    textcard = new { title = message.Title, description = message.Description, url = _options.Get("ServerAddress", AppDefaults.ServerAddress) }
                };
        }
        else if (clientType == "plugin")
        {
            body = new
            {
                msgtype = "textcard",
                touser = toUser,
                agentid = agentId,
                textcard = new { title = message.Title, description = message.Description, url = message.Url }
            };
        }
        else
        {
            body = new { msgtype = "markdown", touser = toUser, agentid = agentId, markdown = new { content = message.Content } };
        }
        var token = _tokens.GetToken(corpId + agentId + channel.Secret);
        var url = $"https://qyapi.weixin.qq.com/cgi-bin/message/send?access_token={token}";
        var (_, text) = await HttpChannelHelpers.PostRawAsync(_http.CreateClient("channels"), url, body, ct);
        var res = JsonSerializer.Deserialize<WxRes>(text, JsonDefaults.Options);
        if (res is null || res.Errcode != 0)
            throw new BusinessException(res?.Errmsg ?? "corp_app failed");
    }

    private sealed class WxRes { public int Errcode { get; set; } public string? Errmsg { get; set; } }
}

public sealed class GroupProvider : IChannelProvider
{
    private readonly IServiceScopeFactory _scopes;
    public string Type => ChannelType.Group;
    public GroupProvider(IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        var subChannels = channel.AppId.Split('|');
        var subTargets = string.IsNullOrEmpty(message.To) ? channel.AccountId.Split('|') : message.To.Split('|');
        if (subChannels.Length != subTargets.Length)
            throw new BusinessException("无效的群组消息配置，子通道数量与子目标数量不一致");

        using var scope = _scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<Abstractions.Repositories.IChannelRepository>();
        var factory = scope.ServiceProvider.GetRequiredService<ChannelProviderFactory>();
        var err = new StringBuilder();
        for (var i = 0; i < subChannels.Length; i++)
        {
            message.To = subTargets[i];
            message.Channel = subChannels[i];
            var sub = await repo.GetByNameAsync(subChannels[i], user.Id, ct)
                      ?? throw new BusinessException("获取群组消息子通道失败：not found");
            if (sub.Type == ChannelType.Group)
                throw new BusinessException("群组消息子通道不能是群组消息");
            try
            {
                await factory.Resolve(sub.Type).SendAsync(message, user, sub, ct);
            }
            catch (Exception ex)
            {
                err.Append($"发送群组消息子通道 {subChannels[i]} 失败：{ex.Message}\n");
            }
        }
        if (err.Length > 0)
            throw new BusinessException(err.ToString());
    }
}
