using System.Text.Json;
using System.Text.Json.Serialization;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Json;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Channels;

public sealed class PushDeerProvider : IChannelProvider
{
    // 默认公有云端点；自建时由 Url 指定。依据官方 PushDeerMessageController：POST {server}/message/push，
    // 表单字段 pushkey/text/desp/type，type 缺省 markdown。成功返回 {code:0,content:...}，
    // 失败返回 {code:"<字符串>",error:"..."}（错误码可能是字符串，故成功判定只认数值 0）。
    internal const string DefaultServer = "https://api2.pushdeer.com";
    private readonly IHttpClientFactory _http;
    private readonly ISystemOptionService _options;
    public string Type => ChannelType.PushDeer;

    public PushDeerProvider(IHttpClientFactory http, ISystemOptionService options)
    {
        _http = http;
        _options = options;
    }

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        // pushkey 已绑定目标设备，凭证即目标：拒绝非空 To（群组中该子通道目标应留空）。
        ChannelTargetPolicy.RejectFixedTargetTo(message, "PushDeer");
        var server = OutboundUrlPolicy.WithDefault(channel.Url, DefaultServer);
        OutboundUrlPolicy.Validate(server, _options, "PushDeer");
        var url = server.TrimEnd('/') + "/message/push";

        var fields = new Dictionary<string, string>
        {
            ["pushkey"] = channel.Secret,
            ["text"] = message.Title,
            ["desp"] = HttpChannelHelpers.ComposeBody(message)
        };
        if (!string.IsNullOrEmpty(channel.Other))
            fields["type"] = channel.Other;

        var body = await HttpChannelHelpers.SendAndReadAsync(_http.CreateClient("channels"), HttpMethod.Post, url,
            new FormUrlEncodedContent(fields), "PushDeer", ct, sensitiveValues: [channel.Secret]);
        if (string.IsNullOrWhiteSpace(body))
            throw new BusinessException("PushDeer 返回空响应");
        var res = HttpChannelHelpers.ParseResponse<PushDeerResponse>(body, "PushDeer");
        if (!res.IsSuccess)
            throw HttpChannelHelpers.BusinessFailure("PushDeer", res.ErrorText, channel.Secret);
    }
}

public sealed class PushDeerResponse
{
    // code 成功时为数值 0，失败时为字符串（如 "9999"/"ARGS"），用 JsonElement 兼容两者。
    [JsonPropertyName("code")] public JsonElement Code { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }

    public bool IsSuccess => Code.ValueKind == JsonValueKind.Number && Code.TryGetInt32(out var c) && c == 0;
    public string? ErrorText => Error ?? Message;
}

public sealed class PushDeerConfigValidator : IChannelConfigValidator
{
    private readonly ISystemOptionService _options;
    public string Type => ChannelType.PushDeer;
    public PushDeerConfigValidator(ISystemOptionService options) => _options = options;

    public void Validate(Channel channel)
    {
        if (string.IsNullOrWhiteSpace(channel.Secret))
            throw new BusinessException("PushDeer pushkey 不能为空");
        if (!string.IsNullOrEmpty(channel.Url))
            OutboundUrlPolicy.Validate(channel.Url, _options, "PushDeer");
        if (!string.IsNullOrEmpty(channel.Other) && channel.Other is not ("text" or "markdown"))
            throw new BusinessException("PushDeer 消息类型只能是 text 或 markdown");
    }
}
