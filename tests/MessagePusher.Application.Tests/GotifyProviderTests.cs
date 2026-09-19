using System.Net;
using System.Text;
using System.Text.Json;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Channels;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Tests;

public class GotifyProviderTests
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
            _responder = responder ?? (() => Ok("{\"id\":123,\"appid\":1,\"message\":\"m\",\"title\":\"t\",\"priority\":0}"));

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

    private static (GotifyProvider Provider, CapturingHandler Handler) Create(
        Func<HttpResponseMessage>? responder = null, Dictionary<string, string>? options = null)
    {
        var handler = new CapturingHandler(responder);
        return (new GotifyProvider(new FakeHttpFactory(handler), new FakeOptions(options)), handler);
    }

    private static JsonElement Json(string body) => JsonDocument.Parse(body).RootElement;
    private static User User() => new() { Id = 1 };
    private static Channel Ch(string url = "https://gotify.example.com", string secret = "APP_TOKEN", string other = "") =>
        new() { Type = ChannelType.Gotify, Url = url, Secret = secret, Other = other };

    [Fact]
    public async Task Posts_json_to_message_path_with_gotify_key_header()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(), CancellationToken.None);
        Assert.Equal("https://gotify.example.com/message", handler.Uri!.ToString());
        Assert.Equal("application/json", handler.ContentType);
        Assert.Equal("APP_TOKEN", handler.Headers["X-Gotify-Key"]);
    }

    [Fact]
    public async Task Trailing_slash_trimmed()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T" }, User(), Ch(url: "https://gotify.example.com/"), CancellationToken.None);
        Assert.Equal("https://gotify.example.com/message", handler.Uri!.ToString());
    }

    [Fact]
    public async Task Title_and_message_mapped_and_url_appended()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "标题", Content = "", Description = "描述", Url = "https://e.com/x" },
            User(), Ch(), CancellationToken.None);
        var json = Json(handler.Body);
        Assert.Equal("标题", json.GetProperty("title").GetString());
        Assert.Equal("描述\n\nhttps://e.com/x", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Priority_emitted_when_configured_omitted_when_empty()
    {
        var (p1, h1) = Create();
        await p1.SendAsync(new Message { Title = "T" }, User(), Ch(other: "7"), CancellationToken.None);
        Assert.Equal(7, Json(h1.Body).GetProperty("priority").GetInt32());

        var (p2, h2) = Create();
        await p2.SendAsync(new Message { Title = "T" }, User(), Ch(other: ""), CancellationToken.None);
        Assert.False(Json(h2.Body).TryGetProperty("priority", out _));
    }

    [Fact]
    public async Task Non_empty_to_rejected_before_request()
    {
        var (provider, handler) = Create();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T", To = "someone" }, User(), Ch(), CancellationToken.None));
        Assert.Contains("固定目标", ex.Message);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Empty_url_throws_before_request()
    {
        var (provider, handler) = Create();
        await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(url: ""), CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Success_requires_positive_id()
    {
        var (provider, _) = Create(() => CapturingHandler.Ok("{\"id\":0,\"error\":\"unauthorized\",\"errorDescription\":\"token invalid\"}"));
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(), CancellationToken.None));
        Assert.Contains("token invalid", ex.Message);
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
        var (provider, _) = Create(() => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":\"unauthorized\",\"errorDescription\":\"bad APP_TOKEN\"}", Encoding.UTF8, "application/json")
        });
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(), CancellationToken.None));
        Assert.DoesNotContain("APP_TOKEN", ex.Message);
    }
}

public class GotifyConfigValidatorTests
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

    private static GotifyConfigValidator V(Dictionary<string, string>? options = null) => new(new FakeOptions(options));

    [Fact]
    public void Empty_token_rejected() =>
        Assert.Throws<BusinessException>(() => V().Validate(new Channel { Type = ChannelType.Gotify, Secret = "", Url = "https://g.example.com" }));

    [Fact]
    public void Empty_url_rejected() =>
        Assert.Throws<BusinessException>(() => V().Validate(new Channel { Type = ChannelType.Gotify, Secret = "T", Url = "" }));

    [Fact]
    public void Valid_config_passes() =>
        V().Validate(new Channel { Type = ChannelType.Gotify, Secret = "T", Url = "https://g.example.com", Other = "5" });

    [Fact]
    public void Non_integer_priority_rejected() =>
        Assert.Throws<BusinessException>(() => V().Validate(new Channel { Type = ChannelType.Gotify, Secret = "T", Url = "https://g.example.com", Other = "high" }));

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void Priority_out_of_range_rejected(int p) =>
        Assert.Throws<BusinessException>(() => V().Validate(new Channel { Type = ChannelType.Gotify, Secret = "T", Url = "https://g.example.com", Other = p.ToString() }));

    [Fact]
    public void Plain_http_url_allowed_with_flag() =>
        V(new() { ["ChannelUrlAllowNonHttps"] = "true" })
            .Validate(new Channel { Type = ChannelType.Gotify, Secret = "T", Url = "http://gotify.lan" });
}
