using System.Net;
using System.Text;
using System.Text.Json;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Channels;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Tests;

public class WxPusherProviderTests
{
    private const string SuccessJson = "{\"code\":1000,\"msg\":\"处理成功\",\"data\":[{\"uid\":\"UID_1\",\"code\":1000,\"status\":\"创建发送任务成功\"}],\"success\":true}";

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? ContentType { get; private set; }
        public string Body { get; private set; } = "";
        public int Calls { get; private set; }
        private readonly Func<HttpResponseMessage> _responder;
        public CapturingHandler(Func<HttpResponseMessage>? responder = null) =>
            _responder = responder ?? (() => Ok(SuccessJson));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responder();
        }

        public static HttpResponseMessage Ok(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed class FakeHttpFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static (WxPusherProvider Provider, CapturingHandler Handler) Create(Func<HttpResponseMessage>? responder = null)
    {
        var handler = new CapturingHandler(responder);
        return (new WxPusherProvider(new FakeHttpFactory(handler)), handler);
    }

    private static JsonElement Json(string body) => JsonDocument.Parse(body).RootElement;
    private static User User() => new() { Id = 1 };
    private static Channel Ch(string secret = "AT_TOKEN", string accountId = "UID_1|UID_2", string other = "") =>
        new() { Type = ChannelType.WxPusher, Secret = secret, AccountId = accountId, Other = other };

    [Fact]
    public async Task Posts_json_to_fixed_endpoint_with_exact_field_names()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(), CancellationToken.None);
        Assert.Equal("https://wxpusher.zjiecode.com/api/send/message", handler.Uri!.ToString());
        Assert.Equal("application/json", handler.ContentType);
        Assert.Contains("\"appToken\":\"AT_TOKEN\"", handler.Body);
        Assert.Contains("\"contentType\":1", handler.Body);
        Assert.Contains("\"topicIds\":[]", handler.Body);
        Assert.DoesNotContain("app_token", handler.Body);
        Assert.DoesNotContain("content_type", handler.Body);
    }

    [Fact]
    public async Task Uids_split_from_account_id_by_pipe()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T" }, User(), Ch(accountId: "UID_A|UID_B"), CancellationToken.None);
        var uids = Json(handler.Body).GetProperty("uids").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["UID_A", "UID_B"], uids);
    }

    [Fact]
    public async Task Content_falls_back_to_description_and_url_separate()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "标题", Content = "", Description = "描述", Url = "https://e.com/x" },
            User(), Ch(), CancellationToken.None);
        var json = Json(handler.Body);
        Assert.Equal("描述", json.GetProperty("content").GetString());
        Assert.Equal("标题", json.GetProperty("summary").GetString());
        Assert.Equal("https://e.com/x", json.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Url_omitted_when_empty()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(), CancellationToken.None);
        Assert.False(Json(handler.Body).TryGetProperty("url", out _));
    }

    [Fact]
    public async Task Topic_ids_and_content_type_from_other_when_to_empty()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T" }, User(), Ch(other: "{\"contentType\":3,\"topicIds\":[11,22]}"), CancellationToken.None);
        var json = Json(handler.Body);
        Assert.Equal(3, json.GetProperty("contentType").GetInt32());
        var topics = json.GetProperty("topicIds").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        Assert.Equal([11, 22], topics);
    }

    [Fact]
    public async Task To_override_uses_only_that_uid_and_drops_topic_ids()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", To = "UID_X|UID_Y" }, User(),
            Ch(accountId: "UID_1", other: "{\"topicIds\":[99]}"), CancellationToken.None);
        var json = Json(handler.Body);
        var uids = json.GetProperty("uids").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["UID_X", "UID_Y"], uids);
        Assert.Equal(0, json.GetProperty("topicIds").GetArrayLength());
    }

    [Fact]
    public async Task No_target_throws_before_request()
    {
        var (provider, handler) = Create();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(accountId: ""), CancellationToken.None));
        Assert.Contains("目标", ex.Message);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Summary_truncated_to_99()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = new string('x', 200), Description = "D" }, User(), Ch(), CancellationToken.None);
        Assert.Equal(99, Json(handler.Body).GetProperty("summary").GetString()!.Length);
    }

    [Fact]
    public async Task Code_not_1000_is_failure_with_msg()
    {
        var (provider, _) = Create(() => CapturingHandler.Ok("{\"code\":1001,\"msg\":\"appToken不正确\"}"));
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(), CancellationToken.None));
        Assert.Contains("appToken不正确", ex.Message);
    }

    [Fact]
    public async Task Partial_failure_in_data_array_not_masked_by_top_level_success()
    {
        var (provider, _) = Create(() => CapturingHandler.Ok(
            "{\"code\":1000,\"msg\":\"处理成功\",\"data\":[{\"uid\":\"UID_1\",\"code\":1000},{\"uid\":\"UID_2\",\"code\":1001,\"status\":\"未关注\"}],\"success\":true}"));
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(), CancellationToken.None));
        Assert.Contains("部分目标发送失败", ex.Message);
        Assert.Contains("UID_2", ex.Message);
    }

    [Fact]
    public async Task Spt_mode_rejected_before_request()
    {
        var (provider, handler) = Create();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(accountId: "", other: "{\"mode\":\"spt\"}"), CancellationToken.None));
        Assert.Contains("SPT", ex.Message);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Non_2xx_redacts_app_token()
    {
        var (provider, _) = Create(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("server error AT_TOKEN", Encoding.UTF8, "text/plain")
        });
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(), CancellationToken.None));
        Assert.DoesNotContain("AT_TOKEN", ex.Message);
    }
}

public class WxPusherConfigValidatorTests
{
    private static WxPusherConfigValidator V() => new();

    [Fact]
    public void Empty_token_rejected() =>
        Assert.Throws<BusinessException>(() => V().Validate(new Channel { Type = ChannelType.WxPusher, Secret = "", AccountId = "UID_1" }));

    [Fact]
    public void Valid_uid_config_passes() =>
        V().Validate(new Channel { Type = ChannelType.WxPusher, Secret = "AT_X", AccountId = "UID_1|UID_2" });

    [Fact]
    public void Valid_topic_only_config_passes() =>
        V().Validate(new Channel { Type = ChannelType.WxPusher, Secret = "AT_X", AccountId = "", Other = "{\"topicIds\":[5]}" });

    [Fact]
    public void No_target_rejected() =>
        Assert.Throws<BusinessException>(() => V().Validate(new Channel { Type = ChannelType.WxPusher, Secret = "AT_X", AccountId = "" }));

    [Fact]
    public void Spt_mode_rejected()
    {
        var ex = Assert.Throws<BusinessException>(() =>
            V().Validate(new Channel { Type = ChannelType.WxPusher, Secret = "SPT_X", Other = "{\"mode\":\"spt\"}" }));
        Assert.Contains("SPT", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Invalid_content_type_rejected(int ct)
    {
        var ex = Assert.Throws<BusinessException>(() =>
            V().Validate(new Channel { Type = ChannelType.WxPusher, Secret = "AT_X", AccountId = "UID_1", Other = $"{{\"contentType\":{ct}}}" }));
        Assert.Contains("contentType", ex.Message);
    }

    [Fact]
    public void Invalid_json_other_rejected() =>
        Assert.Throws<BusinessException>(() => V().Validate(new Channel { Type = ChannelType.WxPusher, Secret = "AT_X", AccountId = "UID_1", Other = "oops" }));
}
