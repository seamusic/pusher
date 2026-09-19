using System.Net;
using System.Text;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Channels;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Tests;

public class PushMeProviderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? ContentType { get; private set; }
        public string Body { get; private set; } = "";
        public int Calls { get; private set; }
        private readonly Func<HttpResponseMessage> _responder;
        public CapturingHandler(Func<HttpResponseMessage>? responder = null) =>
            _responder = responder ?? (() => Text("success"));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responder();
        }

        public static HttpResponseMessage Text(string text) =>
            new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/plain") };
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

    private static (PushMeProvider Provider, CapturingHandler Handler) Create(
        Func<HttpResponseMessage>? responder = null, Dictionary<string, string>? options = null)
    {
        var handler = new CapturingHandler(responder);
        return (new PushMeProvider(new FakeHttpFactory(handler), new FakeOptions(options)), handler);
    }

    private static Dictionary<string, string> ParseForm(string body) =>
        body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(a => WebUtility.UrlDecode(a[0]), a => a.Length > 1 ? WebUtility.UrlDecode(a[1]) : "");

    private static User User() => new() { Id = 1 };
    private static Channel Ch(string url = "", string other = "", string secret = "PUSH_KEY") =>
        new() { Type = ChannelType.PushMe, Secret = secret, Url = url, Other = other };

    [Fact]
    public async Task Empty_url_uses_default_server_root_and_form_post()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(), CancellationToken.None);
        Assert.Equal("https://push.i-i.me/", handler.Uri!.ToString());
        Assert.Equal("application/x-www-form-urlencoded", handler.ContentType);
        var form = ParseForm(handler.Body);
        Assert.Equal("PUSH_KEY", form["push_key"]);
        Assert.Equal("T", form["title"]);
        Assert.Equal("D", form["content"]);
    }

    [Fact]
    public async Task Self_host_url_trimmed_to_root()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(url: "https://push.example.com/"), CancellationToken.None);
        Assert.Equal("https://push.example.com/", handler.Uri!.ToString());
    }

    [Fact]
    public async Task Content_falls_back_to_description_and_appends_url()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Content = "", Description = "描述", Url = "https://e.com/x" },
            User(), Ch(), CancellationToken.None);
        Assert.Equal("描述\n\nhttps://e.com/x", ParseForm(handler.Body)["content"]);
    }

    [Fact]
    public async Task Type_field_sent_only_when_configured()
    {
        var (p1, h1) = Create();
        await p1.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(other: "markdown"), CancellationToken.None);
        Assert.Equal("markdown", ParseForm(h1.Body)["type"]);

        var (p2, h2) = Create();
        await p2.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(other: ""), CancellationToken.None);
        Assert.False(ParseForm(h2.Body).ContainsKey("type"));
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
    public async Task Only_plain_text_success_is_success()
    {
        var (provider, _) = Create(() => CapturingHandler.Text("  success \n"));
        await provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(), CancellationToken.None);
    }

    [Fact]
    public async Task Non_success_text_is_failure_and_surfaces_message()
    {
        var (provider, _) = Create(() => CapturingHandler.Text("invalid push_key"));
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(), CancellationToken.None));
        Assert.Contains("invalid push_key", ex.Message);
    }

    [Fact]
    public async Task Json_like_response_not_deserialized_and_treated_as_error()
    {
        var (provider, _) = Create(() => CapturingHandler.Text("{\"code\":0}"));
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(), CancellationToken.None));
        Assert.Contains("PushMe", ex.Message);
    }

    [Fact]
    public async Task Non_2xx_redacts_push_key()
    {
        var (provider, _) = Create(() => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>bad gateway PUSH_KEY</html>", Encoding.UTF8, "text/html")
        });
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(), CancellationToken.None));
        Assert.DoesNotContain("PUSH_KEY", ex.Message);
    }

    [Fact]
    public async Task Plain_http_self_host_rejected_without_flag()
    {
        var (provider, handler) = Create();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(url: "http://push.lan"), CancellationToken.None));
        Assert.Contains("HTTPS", ex.Message);
        Assert.Equal(0, handler.Calls);
    }
}

public class PushMeConfigValidatorTests
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

    private static PushMeConfigValidator V(Dictionary<string, string>? options = null) => new(new FakeOptions(options));

    [Fact]
    public void Empty_push_key_rejected() =>
        Assert.Throws<BusinessException>(() => V().Validate(new Channel { Type = ChannelType.PushMe, Secret = "" }));

    [Theory]
    [InlineData("text")]
    [InlineData("markdown")]
    [InlineData("")]
    public void Valid_type_passes(string other) =>
        V().Validate(new Channel { Type = ChannelType.PushMe, Secret = "K", Other = other });

    [Fact]
    public void Invalid_type_rejected()
    {
        var ex = Assert.Throws<BusinessException>(() => V().Validate(new Channel { Type = ChannelType.PushMe, Secret = "K", Other = "html" }));
        Assert.Contains("text 或 markdown", ex.Message);
    }

    [Fact]
    public void Plain_http_url_allowed_with_flag() =>
        V(new() { ["ChannelUrlAllowNonHttps"] = "true" })
            .Validate(new Channel { Type = ChannelType.PushMe, Secret = "K", Url = "http://push.lan" });
}
