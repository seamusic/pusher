using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Json;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Channels;

// SendKey 解析与端点路由：同一 Type 支持 Turbo 与 Server酱³ 两个版本，Key 不通用。
// 路由规则依据官方 serverchan-sdk-golang：sctp<uid>t<token> → https://<uid>.push.ft07.com/send/<key>.send，
// 其余（SCT 前缀）→ https://sctapi.ftqq.com/<key>.send。非法 Key 在发起请求前报错，绝不把整个 Key 当域名。
internal static partial class ServerChanKey
{
    private const string Sc3Prefix = "sctp";
    private const string TurboPrefix = "SCT";

    [GeneratedRegex(@"^sctp(\d+)t")]
    private static partial Regex Sc3Uid();

    public static bool IsSc3(string key) =>
        !string.IsNullOrEmpty(key) && key.StartsWith(Sc3Prefix, StringComparison.Ordinal);

    public static bool IsValid(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;
        if (IsSc3(key))
            return Sc3Uid().IsMatch(key);
        return key.StartsWith(TurboPrefix, StringComparison.Ordinal);
    }

    public static (string Url, bool Sc3) ResolveEndpoint(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new BusinessException("Server酱 SendKey 不能为空");
        if (IsSc3(key))
        {
            var m = Sc3Uid().Match(key);
            if (!m.Success)
                throw new BusinessException("Server酱³ SendKey 格式无效，应形如 sctp<数字uid>t<token>");
            return ($"https://{m.Groups[1].Value}.push.ft07.com/send/{key}.send", true);
        }
        if (!key.StartsWith(TurboPrefix, StringComparison.Ordinal))
            throw new BusinessException("Server酱 SendKey 无效：Turbo 以 SCT 开头，Server酱³ 以 sctp 开头，两者不通用");
        return ($"https://sctapi.ftqq.com/{key}.send", false);
    }

    // Other 为非敏感高级选项 JSON：short（Turbo 与 SC3 均可）、tags（仅 SC3，竖线分隔）。
    public static (string Short, string Tags) ParseOptions(string other)
    {
        if (string.IsNullOrWhiteSpace(other))
            return ("", "");
        try
        {
            using var doc = JsonDocument.Parse(other);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new BusinessException("Server酱 高级选项必须是 JSON 对象，例如 {\"short\":\"...\",\"tags\":\"...\"}");
            return (ReadString(doc.RootElement, "short"), ReadString(doc.RootElement, "tags"));
        }
        catch (JsonException)
        {
            throw new BusinessException("Server酱 高级选项不是合法 JSON");
        }
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

public sealed class ServerChanProvider : IChannelProvider
{
    // Turbo 标题上限 32 字符（Server酱 Turbo 文档）；SC3 标题上限未获官方明确数值，故不截断，交由服务端判定。
    private const int TurboTitleMaxLength = 32;
    private readonly IHttpClientFactory _http;
    public string Type => ChannelType.ServerChan;
    public ServerChanProvider(IHttpClientFactory http) => _http = http;

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        var sendKey = channel.Secret;
        var (url, sc3) = ServerChanKey.ResolveEndpoint(sendKey);
        // SC3 无 channel/openid 目标概念，凭证即目标：拒绝非空 To（群组中该子通道目标应留空）。
        if (sc3)
            ChannelTargetPolicy.RejectFixedTargetTo(message, "Server酱³");

        var (shortText, tags) = ServerChanKey.ParseOptions(channel.Other);
        var title = sc3 ? message.Title : HttpChannelHelpers.TruncateUnicode(message.Title, TurboTitleMaxLength);
        var fields = new Dictionary<string, string>
        {
            ["title"] = title,
            ["desp"] = HttpChannelHelpers.ComposeBody(message)
        };
        if (!string.IsNullOrEmpty(shortText))
            fields["short"] = shortText;
        if (sc3)
        {
            if (!string.IsNullOrEmpty(tags))
                fields["tags"] = tags;
        }
        else
        {
            // Turbo：AccountId 对应可选 channel，To 优先覆盖。
            var channelParam = ChannelTargetPolicy.ResolveDynamic(message, channel);
            if (!string.IsNullOrEmpty(channelParam))
                fields["channel"] = channelParam;
        }

        var body = await HttpChannelHelpers.SendAndReadAsync(_http.CreateClient("channels"), HttpMethod.Post, url,
            new FormUrlEncodedContent(fields), "Server酱", ct, sensitiveValues: [sendKey]);
        if (string.IsNullOrWhiteSpace(body))
            throw new BusinessException("Server酱 返回空响应");
        var res = JsonSerializer.Deserialize<ServerChanResponse>(body, OutboundJson.Options);
        if (res is null || res.Code != 0)
            throw new BusinessException(string.IsNullOrEmpty(res?.Message) ? "Server酱 发送失败" : $"Server酱 发送失败：{res!.Message}");
    }
}

public sealed class ServerChanResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

public sealed class ServerChanConfigValidator : IChannelConfigValidator
{
    public string Type => ChannelType.ServerChan;

    public void Validate(Channel channel)
    {
        if (!ServerChanKey.IsValid(channel.Secret))
            throw new BusinessException("Server酱 SendKey 无效：Turbo 以 SCT 开头，Server酱³ 形如 sctp<数字uid>t<token>，两者不通用");
        var sc3 = ServerChanKey.IsSc3(channel.Secret);
        if (sc3 && !string.IsNullOrEmpty(channel.AccountId))
            throw new BusinessException("Server酱³ 为固定目标，不支持指定 channel，请留空 AccountId");
        var (_, tags) = ServerChanKey.ParseOptions(channel.Other);
        if (!sc3 && !string.IsNullOrEmpty(tags))
            throw new BusinessException("tags 仅 Server酱³ 支持，Turbo 请移除 tags 选项");
    }
}
