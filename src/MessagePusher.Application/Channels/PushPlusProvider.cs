using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Json;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Channels;

public sealed partial class PushPlusProvider : IChannelProvider
{
    // 固定官方端点；JSON POST，字段名由 [JsonPropertyName] 固定并经 OutboundJson 出站配置序列化。
    // 依据 pushplus.plus/doc/guide/api.html：{token,title,content,template,topic}，成功 {code:200,msg,data}。
    // code==200 表示"受理成功"，投递异步进行，不代表实际送达条数。
    internal const string SendUrl = "https://www.pushplus.plus/send";
    private const int TitleMaxLength = 100;
    private const int ContentMaxLength = 20000;
    private readonly IHttpClientFactory _http;
    public string Type => ChannelType.PushPlus;
    public PushPlusProvider(IHttpClientFactory http) => _http = http;

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        var topic = ChannelTargetPolicy.ResolveDynamic(message, channel);
        var request = new PushPlusRequest
        {
            Token = channel.Secret,
            Title = HttpChannelHelpers.TruncateUnicode(message.Title, TitleMaxLength),
            Content = HttpChannelHelpers.TruncateUnicode(HttpChannelHelpers.ComposeBody(message), ContentMaxLength),
            Template = string.IsNullOrEmpty(channel.Other) ? null : channel.Other,
            Topic = string.IsNullOrEmpty(topic) ? null : topic
        };

        var body = await HttpChannelHelpers.SendAndReadAsync(_http.CreateClient("channels"), HttpMethod.Post, SendUrl,
            HttpChannelHelpers.JsonContent(request, OutboundJson.Options), "PushPlus", ct, sensitiveValues: [channel.Secret]);
        if (string.IsNullOrWhiteSpace(body))
            throw new BusinessException("PushPlus 返回空响应");
        var res = HttpChannelHelpers.ParseResponse<PushPlusResponse>(body, "PushPlus");
        if (res.Code != 200)
            throw HttpChannelHelpers.BusinessFailure("PushPlus", $"code={res.Code} {res.Msg}", channel.Secret);
    }
}

public sealed class PushPlusRequest
{
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    // 可选项：为空时不写出（覆盖出站配置的 Never，避免下发 null/空值）。
    [JsonPropertyName("template")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Template { get; set; }
    [JsonPropertyName("topic")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Topic { get; set; }
}

public sealed class PushPlusResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("msg")] public string? Msg { get; set; }
}

public sealed partial class PushPlusConfigValidator : IChannelConfigValidator
{
    // 官方模板较多且未穷举核验（html/txt/json/markdown/cloudMonitor/...），
    // 故按标识符结构校验而非写死白名单，避免误拒合法模板或放行注入串。
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex TemplateName();

    public string Type => ChannelType.PushPlus;

    public void Validate(Channel channel)
    {
        if (string.IsNullOrWhiteSpace(channel.Secret))
            throw new BusinessException("PushPlus token 不能为空");
        if (!string.IsNullOrEmpty(channel.Other) && !TemplateName().IsMatch(channel.Other))
            throw new BusinessException("PushPlus 模板名无效（仅字母、数字、下划线，且以字母开头），留空默认 html");
    }
}
