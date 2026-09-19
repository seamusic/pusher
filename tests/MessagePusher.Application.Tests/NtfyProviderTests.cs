using System.Net;
using System.Text;
using System.Text.Json;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Channels;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Tests;

public class NtfyProviderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? ContentType { get; private set; }
        public string Body { get; private set; } = "";
        public Dictionary<string, string> Headers { get; } = new();
        public int Calls { get; private set; }
        private readonly Func<HttpResponseMessage> _responder;
        public CapturingHandler(Func<HttpResponseMessage>? responder = null) =>
            _responder = responder ?? (() => Ok("{\"id\":\"AbCd\",\"time\":1,\"event\":\"message\",\"topic\":\"t\"}"));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            foreach (var h in request.Headers)
                Headers[h.Key] = string.Join(",", h.Value);
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

    private sealed class FakeOptions : ISystemOptionService
    {
        private readonly Dictionary<string, string> _values;
        public FakeOptions(Dictionary<string, string>? values = null) => _values = values ?? new();
        public string Get(string key, string fallback = "") => _values.TryGetValue(key, out var v) ? v : fallback;
        public bool GetBool(string key, bool fallback = false) =>
            _values.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : fallback;
        public int GetInt(string key, int fallback = 0) =>
            _values.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : fallback;
        public IReadOnlyDictionary<string, string> Snapshot() => _values;
        public Task InitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(string key, string value, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static (NtfyProvider Provider, CapturingHandler Handler) Create(
        Func<HttpResponseMessage>? responder = null, Dictionary<string, string>? options = null)
    {
        var handler = new CapturingHandler(responder);
        return (new NtfyProvider(new FakeHttpFactory(handler), new FakeOptions(options)), handler);
    }

    private static JsonElement Json(string body) => JsonDocument.Parse(body).RootElement;
    private static User User() => new() { Id = 1 };
    private static Channel Ch(string accountId = "mytopic", string url = "", string secret = "", string other = "") =>
        new() { Type = ChannelType.Ntfy, AccountId = accountId, Url = url, Secret = secret, Other = other };

    [Fact]
    public async Task Posts_json_to_server_root_not_topic_path()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(), CancellationToken.None);
        Assert.Equal("https://ntfy.sh/", handler.Uri!.ToString());
        Assert.Equal("application/json", handler.ContentType);
        Assert.Equal("mytopic", Json(handler.Body).GetProperty("topic").GetString());
    }

    [Fact]
    public async Task To_overrides_topic()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", To = "override" }, User(), Ch(), CancellationToken.None);
        Assert.Equal("override", Json(handler.Body).GetProperty("topic").GetString());
    }

    [Fact]
    public async Task Empty_topic_throws_before_request()
    {
        var (provider, handler) = Create();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(accountId: ""), CancellationToken.None));
        Assert.Contains("topic", ex.Message);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Bearer_token_added_only_when_secret_present()
    {
        var (p1, h1) = Create();
        await p1.SendAsync(new Message { Title = "T" }, User(), Ch(secret: "tok_123"), CancellationToken.None);
        Assert.Equal("Bearer tok_123", h1.Headers["Authorization"]);

        var (p2, h2) = Create();
        await p2.SendAsync(new Message { Title = "T" }, User(), Ch(secret: ""), CancellationToken.None);
        Assert.False(h2.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task Message_falls_back_to_description_and_url_maps_to_click()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Content = "", Description = "描述", Url = "https://e.com/x" },
            User(), Ch(), CancellationToken.None);
        var json = Json(handler.Body);
        Assert.Equal("描述", json.GetProperty("message").GetString());
        Assert.Equal("https://e.com/x", json.GetProperty("click").GetString());
        // click 承载链接，正文不追加
        Assert.DoesNotContain("https://e.com/x", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Content_preferred_over_description()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Content = "正文", Description = "描述" }, User(), Ch(), CancellationToken.None);
        Assert.Equal("正文", Json(handler.Body).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Title_omitted_when_empty()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "", Description = "D" }, User(), Ch(), CancellationToken.None);
        Assert.False(Json(handler.Body).TryGetProperty("title", out _));
    }

    [Fact]
    public async Task Priority_and_tags_emitted_from_other()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T" }, User(), Ch(other: "{\"priority\":4,\"tags\":\"warning,skull\"}"), CancellationToken.None);
        var json = Json(handler.Body);
        Assert.Equal(4, json.GetProperty("priority").GetInt32());
        var tags = json.GetProperty("tags").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["warning", "skull"], tags);
    }

    [Fact]
    public async Task Tags_array_form_supported()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T" }, User(), Ch(other: "{\"tags\":[\"a\",\"b\"]}"), CancellationToken.None);
        var tags = Json(handler.Body).GetProperty("tags").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["a", "b"], tags);
    }

    [Fact]
    public async Task Optional_fields_omitted_when_unset()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(), CancellationToken.None);
        var json = Json(handler.Body);
        Assert.False(json.TryGetProperty("priority", out _));
        Assert.False(json.TryGetProperty("tags", out _));
        Assert.False(json.TryGetProperty("click", out _));
    }

    [Fact]
    public async Task Chinese_title_serialized_as_utf8_json()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "标题测试", Description = "D" }, User(), Ch(), CancellationToken.None);
        Assert.Equal("标题测试", Json(handler.Body).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Success_requires_message_object()
    {
        var (provider, _) = Create(() => CapturingHandler.Ok("{\"id\":\"x\"}"));
        await provider.SendAsync(new Message { Title = "T" }, User(), Ch(), CancellationToken.None);
    }

    [Fact]
    public async Task Error_object_surfaces_error_text()
    {
        var (provider, _) = Create(() => CapturingHandler.Ok("{\"error\":\"topic not found\"}"));
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(), CancellationToken.None));
        Assert.Contains("topic not found", ex.Message);
    }

    [Fact]
    public async Task Empty_body_is_failure()
    {
        var (provider, _) = Create(() => CapturingHandler.Ok(""));
        await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(), CancellationToken.None));
    }

    [Fact]
    public async Task Non_2xx_redacts_token()
    {
        var (provider, _) = Create(() => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"error\":\"forbidden tok_secret\"}", Encoding.UTF8, "application/json")
        });
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(secret: "tok_secret"), CancellationToken.None));
        Assert.DoesNotContain("tok_secret", ex.Message);
    }

    [Fact]
    public async Task Self_host_url_trims_trailing_slash()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T" }, User(), Ch(url: "https://ntfy.example.com/"), CancellationToken.None);
        Assert.Equal("https://ntfy.example.com/", handler.Uri!.ToString());
    }
}

public class NtfyConfigValidatorTests
{
    private sealed class FakeOptions : ISystemOptionService
    {
        private readonly Dictionary<string, string> _values;
        public FakeOptions(Dictionary<string, string>? values = null) => _values = values ?? new();
        public string Get(string key, string fallback = "") => _values.TryGetValue(key, out var v) ? v : fallback;
        public bool GetBool(string key, bool fallback = false) =>
            _values.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : fallback;
        public int GetInt(string key, int fallback = 0) =>
            _values.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : fallback;
        public IReadOnlyDictionary<string, string> Snapshot() => _values;
        public Task InitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(string key, string value, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static NtfyConfigValidator V(Dictionary<string, string>? options = null) => new(new FakeOptions(options));

    [Fact]
    public void Empty_topic_rejected() =>
        Assert.Throws<BusinessException>(() => V().Validate(new Channel { Type = ChannelType.Ntfy, AccountId = "" }));

    [Fact]
    public void Valid_config_passes() =>
        V().Validate(new Channel { Type = ChannelType.Ntfy, AccountId = "t", Other = "{\"priority\":5}" });

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Priority_out_of_range_rejected(int p)
    {
        var ex = Assert.Throws<BusinessException>(() =>
            V().Validate(new Channel { Type = ChannelType.Ntfy, AccountId = "t", Other = $"{{\"priority\":{p}}}" }));
        Assert.Contains("1~5", ex.Message);
    }

    [Fact]
    public void Invalid_json_other_rejected() =>
        Assert.Throws<BusinessException>(() => V().Validate(new Channel { Type = ChannelType.Ntfy, AccountId = "t", Other = "not-json" }));

    [Fact]
    public void Plain_http_url_rejected_without_flag() =>
        Assert.Throws<BusinessException>(() => V().Validate(new Channel { Type = ChannelType.Ntfy, AccountId = "t", Url = "http://ntfy.lan" }));

    [Fact]
    public void Plain_http_url_allowed_with_flag() =>
        V(new() { ["ChannelUrlAllowNonHttps"] = "true" })
            .Validate(new Channel { Type = ChannelType.Ntfy, AccountId = "t", Url = "http://ntfy.lan" });
}
