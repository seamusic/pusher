using System.Text.Json;
using System.Text.Json.Serialization;

namespace MessagePusher.Application.Json;

// 出站（发往第三方服务）专用序列化配置：字段名完全由 DTO 上的 [JsonPropertyName] 决定，
// 不套用任何命名策略，避免本项目对外 API 的 snake_case 契约污染第三方协议字段
// （如 WxPusher 的 appToken/contentType/topicIds、PushPlus 的 template）。
// 本项目对外 API 契约仍由 JsonDefaults 保持不变。
public static class OutboundJson
{
    public static readonly JsonSerializerOptions Options = Create();

    public static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };
        return o;
    }
}
