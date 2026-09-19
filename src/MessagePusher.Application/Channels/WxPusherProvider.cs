using System.Text.Json;
using System.Text.Json.Serialization;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Json;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Channels;

// WxPusher 标准模式非敏感选项解析：Other 为 JSON {mode,contentType,topicIds}。
// mode 缺省 "standard"；"spt" 协议细节尚未核实，明确拒绝而非塞进标准请求。
internal static class WxPusherOptions
{
    public const string ModeStandard = "standard";
    public const string ModeSpt = "spt";

    public static (string Mode, int? ContentType, int[] TopicIds) Parse(string other)
    {
        if (string.IsNullOrWhiteSpace(other))
            return (ModeStandard, null, []);
        try
        {
            using var doc = JsonDocument.Parse(other);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new BusinessException("WxPusher 高级选项必须是 JSON 对象，例如 {\"contentType\":3,\"topicIds\":[123]}");
            var root = doc.RootElement;

            var mode = ModeStandard;
            if (root.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String)
                mode = string.IsNullOrWhiteSpace(m.GetString()) ? ModeStandard : m.GetString()!.Trim().ToLowerInvariant();

            int? contentType = null;
            if (root.TryGetProperty("contentType", out var ct))
            {
                if (ct.ValueKind != JsonValueKind.Number || !ct.TryGetInt32(out var ctv))
                    throw new BusinessException("WxPusher contentType 必须是整数（1 文本 / 2 html / 3 markdown）");
                contentType = ctv;
            }

            var topicIds = new List<int>();
            if (root.TryGetProperty("topicIds", out var t))
            {
                if (t.ValueKind != JsonValueKind.Array)
                    throw new BusinessException("WxPusher topicIds 必须是整数数组");
                foreach (var item in t.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var id))
                        throw new BusinessException("WxPusher topicIds 元素必须是整数");
                    topicIds.Add(id);
                }
            }
            return (mode, contentType, topicIds.ToArray());
        }
        catch (JsonException)
        {
            throw new BusinessException("WxPusher 高级选项不是合法 JSON");
        }
    }

    public static void ValidateContentType(int? contentType)
    {
        if (contentType is not (null or 1 or 2 or 3))
            throw new BusinessException("WxPusher contentType 只能取 1（文本）、2（html）或 3（markdown）");
    }

    // AccountId / To 使用 '|' 分隔多个 UID，同时容忍逗号。
    public static string[] SplitUids(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(['|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed class WxPusherProvider : IChannelProvider
{
    // 固定官方端点，JSON POST。依据 wxpusher.zjiecode.com/docs：
    // 请求 {appToken,content,summary,contentType,uids,topicIds,url}，成功 code==1000；
    // 顶层成功不等于全部目标送达，需检查 data.fails 部分失败明细。
    internal const string SendUrl = "https://wxpusher.zjiecode.com/api/send/message";
    private const int SummaryMaxLength = 99;
    private readonly IHttpClientFactory _http;
    public string Type => ChannelType.WxPusher;
    public WxPusherProvider(IHttpClientFactory http) => _http = http;

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        var (mode, contentType, topicIds) = WxPusherOptions.Parse(channel.Other);
        if (mode == WxPusherOptions.ModeSpt)
            throw new BusinessException("WxPusher SPT 模式协议待核验，尚未开放，请使用标准模式（appToken + UID/topicIds）");
        if (mode != WxPusherOptions.ModeStandard)
            throw new BusinessException($"WxPusher 不支持的模式：{mode}");

        // To 非空时仅使用该 UID 列表，且不附加配置 topicIds，避免定向发送仍广播到默认主题。
        string[] uids;
        int[] effectiveTopicIds;
        if (!string.IsNullOrWhiteSpace(message.To))
        {
            uids = WxPusherOptions.SplitUids(message.To);
            effectiveTopicIds = [];
        }
        else
        {
            uids = WxPusherOptions.SplitUids(channel.AccountId);
            effectiveTopicIds = topicIds;
        }
        if (uids.Length == 0 && effectiveTopicIds.Length == 0)
            throw new BusinessException("WxPusher 缺少发送目标，请配置 AccountId(UID) 或 topicIds，或在消息中指定 To");

        // url 使用独立字段，正文不追加；content 用 Content 空则 Description 的纯 fallback。
        var content = string.IsNullOrEmpty(message.Content) ? message.Description : message.Content;
        var request = new WxPusherRequest
        {
            AppToken = channel.Secret,
            Content = content,
            Summary = HttpChannelHelpers.TruncateUnicode(message.Title, SummaryMaxLength),
            ContentType = contentType ?? 1,
            Uids = uids,
            TopicIds = effectiveTopicIds,
            Url = string.IsNullOrEmpty(message.Url) ? null : message.Url
        };

        var body = await HttpChannelHelpers.SendAndReadAsync(_http.CreateClient("channels"), HttpMethod.Post, SendUrl,
            HttpChannelHelpers.JsonContent(request, OutboundJson.Options), "WxPusher", ct, sensitiveValues: [channel.Secret]);
        if (string.IsNullOrWhiteSpace(body))
            throw new BusinessException("WxPusher 返回空响应");
        var res = JsonSerializer.Deserialize<WxPusherResponse>(body, OutboundJson.Options);
        if (res is null || res.Code != 1000)
            throw new BusinessException(string.IsNullOrEmpty(res?.Msg) ? "WxPusher 发送失败" : $"WxPusher 发送失败：{res!.Msg}");

        // 顶层 code==1000 后再检查逐目标结果，部分失败不能被顶层成功覆盖。
        var fails = res.ExtractFails();
        if (fails.Count > 0)
            throw new BusinessException($"WxPusher 部分目标发送失败（{fails.Count}）：{string.Join("; ", fails)}");
    }
}

public sealed class WxPusherRequest
{
    [JsonPropertyName("appToken")] public string AppToken { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("contentType")] public int ContentType { get; set; } = 1;
    [JsonPropertyName("uids")] public string[] Uids { get; set; } = [];
    [JsonPropertyName("topicIds")] public int[] TopicIds { get; set; } = [];
    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }
}

public sealed class WxPusherResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("msg")] public string? Msg { get; set; }
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("data")] public JsonElement Data { get; set; }

    // data 结构未获官方逐字段固化，防御式提取失败明细：识别 fails 数组（对象含 uid/reason 或纯字符串）。
    public List<string> ExtractFails()
    {
        var result = new List<string>();
        if (Data.ValueKind != JsonValueKind.Object || !Data.TryGetProperty("fails", out var fails))
            return result;
        if (fails.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in fails.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    result.Add(item.GetString() ?? "");
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    var uid = item.TryGetProperty("uid", out var u) ? u.GetString() : null;
                    var reason = item.TryGetProperty("reason", out var r) ? r.GetString() : null;
                    result.Add(string.IsNullOrEmpty(uid) ? (reason ?? "未知目标") : $"{uid}:{reason}");
                }
            }
        }
        else if (fails.ValueKind == JsonValueKind.Number && fails.TryGetInt32(out var n) && n > 0)
        {
            result.Add($"失败 {n} 个目标");
        }
        return result;
    }
}

public sealed class WxPusherConfigValidator : IChannelConfigValidator
{
    public string Type => ChannelType.WxPusher;

    public void Validate(Channel channel)
    {
        if (string.IsNullOrWhiteSpace(channel.Secret))
            throw new BusinessException("WxPusher appToken 不能为空");
        var (mode, contentType, topicIds) = WxPusherOptions.Parse(channel.Other);
        if (mode == WxPusherOptions.ModeSpt)
            throw new BusinessException("WxPusher SPT 模式协议待核验，尚未开放，请先使用标准模式");
        if (mode != WxPusherOptions.ModeStandard)
            throw new BusinessException($"WxPusher 不支持的模式：{mode}（仅 standard）");
        WxPusherOptions.ValidateContentType(contentType);
        if (WxPusherOptions.SplitUids(channel.AccountId).Length == 0 && topicIds.Length == 0)
            throw new BusinessException("WxPusher 必须至少配置一个 UID（AccountId，多个用 | 分隔）或 topicIds");
    }
}
