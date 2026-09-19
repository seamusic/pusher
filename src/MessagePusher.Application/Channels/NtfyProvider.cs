using System.Text.Json;
using System.Text.Json.Serialization;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Json;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Channels;

// ntfy 高级选项解析：Other 为非敏感 JSON，支持 priority（1~5）与 tags（逗号分隔字符串或字符串数组）。
internal static class NtfyOptions
{
    public static (int? Priority, string[] Tags) Parse(string other)
    {
        if (string.IsNullOrWhiteSpace(other))
            return (null, []);
        try
        {
            using var doc = JsonDocument.Parse(other);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new BusinessException("ntfy 高级选项必须是 JSON 对象，例如 {\"priority\":3,\"tags\":\"warning\"}");
            var root = doc.RootElement;

            int? priority = null;
            if (root.TryGetProperty("priority", out var p))
            {
                if (p.ValueKind != JsonValueKind.Number || !p.TryGetInt32(out var pv))
                    throw new BusinessException("ntfy priority 必须是整数（1~5）");
                priority = pv;
            }

            var tags = new List<string>();
            if (root.TryGetProperty("tags", out var t))
            {
                if (t.ValueKind == JsonValueKind.String)
                    tags.AddRange((t.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                else if (t.ValueKind == JsonValueKind.Array)
                    foreach (var item in t.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                            tags.Add(item.GetString()!.Trim());
                else
                    throw new BusinessException("ntfy tags 必须是字符串或字符串数组");
            }
            return (priority, tags.ToArray());
        }
        catch (JsonException)
        {
            throw new BusinessException("ntfy 高级选项不是合法 JSON");
        }
    }

    public static void ValidatePriority(int? priority)
    {
        if (priority is < 1 or > 5)
            throw new BusinessException("ntfy priority 只能取 1~5（1 最低，5 最高）");
    }
}

public sealed class NtfyProvider : IChannelProvider
{
    // 向服务器根路径发送 JSON，不向 /{topic} POST JSON。默认官方服务，自建时由 Url 指定。
    // 依据 docs.ntfy.sh/publish（publish-as-json）：请求 {topic,title,message,click,priority,tags}，
    // 成功返回已发布的消息对象（含 id/time/event=message）；错误由非 2xx + {code,http,error} 表达。
    internal const string DefaultServer = "https://ntfy.sh";
    private readonly IHttpClientFactory _http;
    private readonly ISystemOptionService _options;
    public string Type => ChannelType.Ntfy;

    public NtfyProvider(IHttpClientFactory http, ISystemOptionService options)
    {
        _http = http;
        _options = options;
    }

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        // topic 为动态目标：To 优先覆盖 AccountId。
        var topic = ChannelTargetPolicy.ResolveDynamic(message, channel);
        if (string.IsNullOrWhiteSpace(topic))
            throw new BusinessException("ntfy 缺少 topic，请配置 AccountId 或在消息中指定 To");

        var server = OutboundUrlPolicy.WithDefault(channel.Url, DefaultServer);
        OutboundUrlPolicy.Validate(server, _options, "ntfy");

        var (priority, tags) = NtfyOptions.Parse(channel.Other);
        // click 承载链接，正文不再追加 Url；message 用 Content 空则 Description 的纯 fallback。
        var body = string.IsNullOrEmpty(message.Content) ? message.Description : message.Content;
        var request = new NtfyRequest
        {
            Topic = topic,
            Title = string.IsNullOrEmpty(message.Title) ? null : message.Title,
            Message = body,
            Click = string.IsNullOrEmpty(message.Url) ? null : message.Url,
            Priority = priority,
            Tags = tags.Length == 0 ? null : tags
        };

        Dictionary<string, string>? headers = null;
        if (!string.IsNullOrEmpty(channel.Secret))
            headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + channel.Secret };

        var text = await HttpChannelHelpers.SendAndReadAsync(_http.CreateClient("channels"), HttpMethod.Post, server,
            HttpChannelHelpers.JsonContent(request, OutboundJson.Options), "ntfy", ct, headers, sensitiveValues: [channel.Secret]);
        if (string.IsNullOrWhiteSpace(text))
            throw new BusinessException("ntfy 返回空响应");
        var res = JsonSerializer.Deserialize<NtfyResponse>(text, OutboundJson.Options);
        // 成功判定依据消息对象结构（id/event=message），而非通用 code==0/200。
        if (res is null || (string.IsNullOrEmpty(res.Id) && res.Event != "message"))
        {
            var err = res?.Error;
            throw new BusinessException(string.IsNullOrEmpty(err) ? "ntfy 发送失败：响应不是有效的消息对象" : $"ntfy 发送失败：{err}");
        }
    }
}

public sealed class NtfyRequest
{
    [JsonPropertyName("topic")] public string Topic { get; set; } = "";
    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("click")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Click { get; set; }
    [JsonPropertyName("priority")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Priority { get; set; }
    [JsonPropertyName("tags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Tags { get; set; }
}

public sealed class NtfyResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("event")] public string? Event { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public sealed class NtfyConfigValidator : IChannelConfigValidator
{
    private readonly ISystemOptionService _options;
    public string Type => ChannelType.Ntfy;
    public NtfyConfigValidator(ISystemOptionService options) => _options = options;

    public void Validate(Channel channel)
    {
        if (string.IsNullOrWhiteSpace(channel.AccountId))
            throw new BusinessException("ntfy 必须提供 topic（AccountId）");
        if (!string.IsNullOrEmpty(channel.Url))
            OutboundUrlPolicy.Validate(channel.Url, _options, "ntfy");
        var (priority, _) = NtfyOptions.Parse(channel.Other);
        NtfyOptions.ValidatePriority(priority);
    }
}
