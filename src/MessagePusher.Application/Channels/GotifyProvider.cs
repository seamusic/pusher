using System.Text.Json;
using System.Text.Json.Serialization;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Json;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Channels;

public sealed class GotifyProvider : IChannelProvider
{
    // 自托管服务，无公有默认地址，必须由 Url 指定。依据 gotify.net/docs/pushmsg：
    // JSON POST {server}/message，Header X-Gotify-Key 承载 App Token，请求 {title,message,priority}，
    // 成功返回消息对象 {id,appid,message,title,priority,date}；错误由非 2xx + {error,errorCode,errorDescription} 表达。
    private readonly IHttpClientFactory _http;
    private readonly ISystemOptionService _options;
    public string Type => ChannelType.Gotify;

    public GotifyProvider(IHttpClientFactory http, ISystemOptionService options)
    {
        _http = http;
        _options = options;
    }

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        // App Token 绑定具体应用，凭证即目标：拒绝非空 To。
        ChannelTargetPolicy.RejectFixedTargetTo(message, "Gotify");
        OutboundUrlPolicy.Validate(channel.Url, _options, "Gotify");
        var url = channel.Url.TrimEnd('/') + "/message";

        var request = new GotifyRequest
        {
            Title = message.Title,
            Message = HttpChannelHelpers.ComposeBody(message),
            Priority = GotifyOptions.ParsePriority(channel.Other)
        };
        var headers = new Dictionary<string, string> { ["X-Gotify-Key"] = channel.Secret };

        var body = await HttpChannelHelpers.SendAndReadAsync(_http.CreateClient("channels"), HttpMethod.Post, url,
            HttpChannelHelpers.JsonContent(request, OutboundJson.Options), "Gotify", ct, headers, sensitiveValues: [channel.Secret]);
        if (string.IsNullOrWhiteSpace(body))
            throw new BusinessException("Gotify 返回空响应");
        var res = JsonSerializer.Deserialize<GotifyResponse>(body, OutboundJson.Options);
        // 成功判定依据 Gotify 自身消息对象（id>0），不套用其他服务的 code 模型。
        if (res is null || res.Id <= 0)
        {
            var err = res is null ? null : (res.ErrorDescription ?? res.Error);
            throw new BusinessException(string.IsNullOrEmpty(err) ? "Gotify 发送失败：响应不是有效的消息对象" : $"Gotify 发送失败：{err}");
        }
    }
}

internal static class GotifyOptions
{
    // Other 为 priority 整数字符串；留空表示不指定（服务端默认 0）。
    public static int? ParsePriority(string other)
    {
        if (string.IsNullOrWhiteSpace(other))
            return null;
        if (!int.TryParse(other.Trim(), out var p))
            throw new BusinessException("Gotify priority 必须是整数");
        return p;
    }

    public static void Validate(string other)
    {
        var p = ParsePriority(other);
        if (p is < 0 or > 10)
            throw new BusinessException("Gotify priority 只能取 0~10");
    }
}

public sealed class GotifyRequest
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("priority")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Priority { get; set; }
}

public sealed class GotifyResponse
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("errorCode")] public string? ErrorCode { get; set; }
    [JsonPropertyName("errorDescription")] public string? ErrorDescription { get; set; }
}

public sealed class GotifyConfigValidator : IChannelConfigValidator
{
    private readonly ISystemOptionService _options;
    public string Type => ChannelType.Gotify;
    public GotifyConfigValidator(ISystemOptionService options) => _options = options;

    public void Validate(Channel channel)
    {
        if (string.IsNullOrWhiteSpace(channel.Secret))
            throw new BusinessException("Gotify App Token 不能为空");
        // 无公有默认服务，Url 必填并校验（含协议与自建地址策略）。
        OutboundUrlPolicy.Validate(channel.Url, _options, "Gotify");
        GotifyOptions.Validate(channel.Other);
    }
}
