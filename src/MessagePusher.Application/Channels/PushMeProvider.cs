using MessagePusher.Application.Abstractions;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Channels;

public sealed class PushMeProvider : IChannelProvider
{
    // 表单 POST 服务根地址，默认官方服务，自建时由 Url 指定。依据 push.i-i.me/docs：
    // 字段 push_key/title/content/type，原生接口成功返回纯文本 "success"，其他文本为错误，不做 JSON 反序列化。
    internal const string DefaultServer = "https://push.i-i.me";
    private readonly IHttpClientFactory _http;
    private readonly ISystemOptionService _options;
    public string Type => ChannelType.PushMe;

    public PushMeProvider(IHttpClientFactory http, ISystemOptionService options)
    {
        _http = http;
        _options = options;
    }

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        // push_key 绑定接收端，凭证即目标：拒绝非空 To。
        ChannelTargetPolicy.RejectFixedTargetTo(message, "PushMe");
        var server = OutboundUrlPolicy.WithDefault(channel.Url, DefaultServer);
        OutboundUrlPolicy.Validate(server, _options, "PushMe");

        var fields = new Dictionary<string, string>
        {
            ["push_key"] = channel.Secret,
            ["title"] = message.Title,
            ["content"] = HttpChannelHelpers.ComposeBody(message)
        };
        if (!string.IsNullOrEmpty(channel.Other))
            fields["type"] = channel.Other;

        var body = await HttpChannelHelpers.SendAndReadAsync(_http.CreateClient("channels"), HttpMethod.Post, server,
            new FormUrlEncodedContent(fields), "PushMe", ct, sensitiveValues: [channel.Secret]);
        // HTTP 状态通过后，按原生纯文本协议判断：仅 "success" 为成功，其余文本原样作为错误回显（脱敏）。
        if (!string.Equals(body.Trim(), "success", StringComparison.OrdinalIgnoreCase))
        {
            var err = SecretMask.Redact(HttpChannelHelpers.StripToPlainText(body), channel.Secret);
            if (err.Length > 200)
                err = err[..200] + "…";
            throw new BusinessException(string.IsNullOrWhiteSpace(err) ? "PushMe 发送失败" : $"PushMe 发送失败：{err}");
        }
    }
}

public sealed class PushMeConfigValidator : IChannelConfigValidator
{
    private readonly ISystemOptionService _options;
    public string Type => ChannelType.PushMe;
    public PushMeConfigValidator(ISystemOptionService options) => _options = options;

    public void Validate(Channel channel)
    {
        if (string.IsNullOrWhiteSpace(channel.Secret))
            throw new BusinessException("PushMe push_key 不能为空");
        if (!string.IsNullOrEmpty(channel.Url))
            OutboundUrlPolicy.Validate(channel.Url, _options, "PushMe");
        // 首版仅原生 text/markdown，不含企微/钉钉/飞书兼容或数据小屏类型。
        if (!string.IsNullOrEmpty(channel.Other) && channel.Other is not ("text" or "markdown"))
            throw new BusinessException("PushMe 消息类型只能是 text 或 markdown");
    }
}
