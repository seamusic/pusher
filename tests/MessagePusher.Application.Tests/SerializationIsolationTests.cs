using System.Text.Json;
using System.Text.Json.Serialization;
using MessagePusher.Application.Channels;
using MessagePusher.Application.Json;

namespace MessagePusher.Application.Tests;

public class SerializationIsolationTests
{
    // 以 WxPusher 标准模式字段为样例的出站 DTO：协议字段名由 [JsonPropertyName] 固定。
    private sealed class WxPusherStyleRequest
    {
        [JsonPropertyName("appToken")] public string AppToken { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
        [JsonPropertyName("summary")] public string Summary { get; set; } = "";
        [JsonPropertyName("contentType")] public int ContentType { get; set; }
        [JsonPropertyName("uids")] public List<string> Uids { get; set; } = [];
        [JsonPropertyName("topicIds")] public List<int> TopicIds { get; set; } = [];
        [JsonPropertyName("url")] public string Url { get; set; } = "";
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string LastBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        }
    }

    [Fact]
    public void Outbound_options_emit_exact_protocol_field_names()
    {
        var dto = new WxPusherStyleRequest
        {
            AppToken = "AT",
            Content = "C",
            Summary = "S",
            ContentType = 1,
            Uids = ["UID_1"],
            TopicIds = [2],
            Url = "https://example.com"
        };

        var json = JsonSerializer.Serialize(dto, OutboundJson.Options);

        Assert.Contains("\"appToken\":\"AT\"", json);
        Assert.Contains("\"contentType\":1", json);
        Assert.Contains("\"topicIds\":[2]", json);
        Assert.Contains("\"uids\":[\"UID_1\"]", json);
        Assert.DoesNotContain("app_token", json);
        Assert.DoesNotContain("content_type", json);
        Assert.DoesNotContain("topic_ids", json);
        Assert.DoesNotContain("AppToken", json);
    }

    [Fact]
    public void JsonDefaults_keep_snake_case_for_project_api_contract()
    {
        var json = JsonSerializer.Serialize(new { AppToken = "AT", ContentType = 1 }, JsonDefaults.Options);
        Assert.Contains("\"app_token\":\"AT\"", json);
        Assert.Contains("\"content_type\":1", json);
    }

    [Fact]
    public async Task PostRawAsync_default_serialization_stays_snake_case()
    {
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);

        var (resp, _) = await HttpChannelHelpers.PostRawAsync(client, "https://example.com",
            new { chat_id = "123", parse_mode = "" }, CancellationToken.None);
        using (resp);

        Assert.Contains("\"chat_id\":\"123\"", handler.LastBody);
        Assert.Contains("\"parse_mode\":\"\"", handler.LastBody);
    }

    [Fact]
    public async Task PostRawAsync_honors_explicit_outbound_options()
    {
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);

        var (resp, _) = await HttpChannelHelpers.PostRawAsync(client, "https://example.com",
            new WxPusherStyleRequest { AppToken = "AT", Content = "C" }, CancellationToken.None,
            options: OutboundJson.Options);
        using (resp);

        Assert.Contains("\"appToken\":\"AT\"", handler.LastBody);
        Assert.DoesNotContain("app_token", handler.LastBody);
    }

    [Fact]
    public void JsonContent_default_uses_snake_case_and_explicit_options_override()
    {
        var defaultContent = HttpChannelHelpers.JsonContent(new { ChatId = "1" });
        Assert.Contains("\"chat_id\":\"1\"", defaultContent.ReadAsStringAsync().Result);

        var outboundContent = HttpChannelHelpers.JsonContent(
            new WxPusherStyleRequest { AppToken = "AT" }, OutboundJson.Options);
        var body = outboundContent.ReadAsStringAsync().Result;
        Assert.Contains("\"appToken\":\"AT\"", body);
        Assert.DoesNotContain("app_token", body);
    }
}
