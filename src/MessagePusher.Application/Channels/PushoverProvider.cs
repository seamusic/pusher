using System.Text.Json;
using System.Text.Json.Serialization;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Json;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Channels;

public sealed class PushoverProvider : IChannelProvider
{
    // 固定官方端点，表单 POST。依据 pushover.net/api：字段 token/user/message/title/url，
    // 成功返回 {"status":1,"request":"..."}，失败经非 2xx + {"errors":[...]} 表达。
    // 长度上限：title 250、message 1024、url 512（超限截断，避免整条被服务端拒绝）。
    internal const string SendUrl = "https://api.pushover.net/1/messages.json";
    private const int TitleMaxLength = 250;
    private const int MessageMaxLength = 1024;
    private const int UrlMaxLength = 512;
    private readonly IHttpClientFactory _http;
    public string Type => ChannelType.Pushover;
    public PushoverProvider(IHttpClientFactory http) => _http = http;

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        // user key 为动态目标：To 优先覆盖 AccountId。
        var userKey = ChannelTargetPolicy.ResolveDynamic(message, channel);
        if (string.IsNullOrWhiteSpace(userKey))
            throw new BusinessException("Pushover 缺少 user key，请配置 AccountId 或在消息中指定 To");

        // url 使用独立字段，正文不再追加。
        var body = string.IsNullOrEmpty(message.Content) ? message.Description : message.Content;
        var fields = new Dictionary<string, string>
        {
            ["token"] = channel.Secret,
            ["user"] = userKey,
            ["message"] = HttpChannelHelpers.TruncateUnicode(body, MessageMaxLength)
        };
        if (!string.IsNullOrEmpty(message.Title))
            fields["title"] = HttpChannelHelpers.TruncateUnicode(message.Title, TitleMaxLength);
        if (!string.IsNullOrEmpty(message.Url))
            fields["url"] = HttpChannelHelpers.TruncateUnicode(message.Url, UrlMaxLength);

        var text = await HttpChannelHelpers.SendAndReadAsync(_http.CreateClient("channels"), HttpMethod.Post, SendUrl,
            new FormUrlEncodedContent(fields), "Pushover", ct, sensitiveValues: [channel.Secret, userKey]);
        if (string.IsNullOrWhiteSpace(text))
            throw new BusinessException("Pushover 返回空响应");
        var res = HttpChannelHelpers.ParseResponse<PushoverResponse>(text, "Pushover");
        if (res.Status != 1 || res.Errors is { Length: > 0 })
        {
            var err = res.Errors is { Length: > 0 } e ? string.Join("; ", e) : null;
            throw HttpChannelHelpers.BusinessFailure("Pushover", err, channel.Secret, userKey);
        }
    }
}

public sealed class PushoverResponse
{
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("errors")] public string[]? Errors { get; set; }
    [JsonPropertyName("request")] public string? Request { get; set; }
}

public sealed class PushoverConfigValidator : IChannelConfigValidator
{
    public string Type => ChannelType.Pushover;

    public void Validate(Channel channel)
    {
        if (string.IsNullOrWhiteSpace(channel.Secret))
            throw new BusinessException("Pushover API Token/Key 不能为空");
        if (string.IsNullOrWhiteSpace(channel.AccountId))
            throw new BusinessException("Pushover User Key 不能为空（AccountId）");
    }
}
