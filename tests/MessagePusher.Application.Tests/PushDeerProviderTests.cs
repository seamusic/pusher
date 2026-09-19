using System.Net;
using System.Text;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Channels;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Tests;

public class PushDeerProviderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? ContentType { get; private set; }
        public string Body { get; private set; } = "";
        public int Calls { get; private set; }
        private readonly Func<HttpResponseMessage> _responder;
        public CapturingHandler(Func<HttpResponseMessage>? responder = null) =>
            _responder = responder ?? (() => Ok("{\"code\":0,\"content\":{\"result\":[]}}"));

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

    private static (PushDeerProvider Provider, CapturingHandler Handler) Create(
        Func<HttpResponseMessage>? responder = null, Dictionary<string, string>? options = null)
    {
        var handler = new CapturingHandler(responder);
        return (new PushDeerProvider(new FakeHttpFactory(handler), new FakeOptions(options)), handler);
    }

    private static Dictionary<string, string> ParseForm(string body) =>
        body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(a => WebUtility.UrlDecode(a[0]), a => a.Length > 1 ? WebUtility.UrlDecode(a[1]) : "");

    private static User User() => new() { Id = 1 };
    private static Channel Ch(string url = "", string other = "", string secret = "PUSHKEY123") =>
        new() { Type = ChannelType.PushDeer, Secret = secret, Url = url, Other = other };

    [Fact]
    public async Task Empty_url_uses_default_server_and_form_post()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(), CancellationToken.None);

        Assert.Equal("https://api2.pushdeer.com/message/push", handler.Uri!.ToString());
        Assert.Equal("application/x-www-form-urlencoded", handler.ContentType);
        var form = ParseForm(handler.Body);
        Assert.Equal("PUSHKEY123", form["pushkey"]);
        Assert.Equal("T", form["text"]);
    }

    [Fact]
    public async Task Self_hosted_url_trims_trailing_slash()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(url: "https://my.pushdeer.com/"), CancellationToken.None);
        Assert.Equal("https://my.pushdeer.com/message/push", handler.Uri!.ToString());
    }

    [Fact]
    public async Task Desp_falls_back_to_description_and_appends_url()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Content = "", Description = "描述", Url = "https://e.com/x" },
            User(), Ch(), CancellationToken.None);
        Assert.Equal("描述\n\nhttps://e.com/x", ParseForm(handler.Body)["desp"]);
    }

    [Fact]
    public async Task Type_field_sent_only_when_configured()
    {
        var (p1, h1) = Create();
        await p1.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(other: "text"), CancellationToken.None);
        Assert.Equal("text", ParseForm(h1.Body)["type"]);

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
    public async Task Chinese_and_special_chars_form_encoded_correctly()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "标题 空格&符号=测试", Description = "内容" }, User(), Ch(), CancellationToken.None);
        Assert.Equal("标题 空格&符号=测试", ParseForm(handler.Body)["text"]);
    }

    [Fact]
    public async Task String_error_code_is_failure_and_surfaces_error_text()
    {
        var (provider, _) = Create(() => CapturingHandler.Ok("{\"code\":\"9999\",\"error\":\"此账号已被停用\"}"));
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(), CancellationToken.None));
        Assert.Contains("此账号已被停用", ex.Message);
    }

    [Fact]
    public async Task Empty_body_is_failure()
    {
        var (provider, _) = Create(() => CapturingHandler.Ok(""));
        await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(), CancellationToken.None));
    }

    [Fact]
    public async Task Non_2xx_html_redacts_pushkey()
    {
        var (provider, _) = Create(() => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>bad gateway PUSHKEY123</html>", Encoding.UTF8, "text/html")
        });
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(), CancellationToken.None));
        Assert.DoesNotContain("PUSHKEY123", ex.Message);
        Assert.Contains("***", ex.Message);
    }

    [Fact]
    public async Task Plain_http_self_host_rejected_without_flag()
    {
        var (provider, handler) = Create();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(url: "http://lan.pushdeer.com"), CancellationToken.None));
        Assert.Contains("HTTPS", ex.Message);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Plain_http_self_host_allowed_with_flag()
    {
        var (provider, handler) = Create(options: new() { ["ChannelUrlAllowNonHttps"] = "true" });
        await provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(url: "http://lan.pushdeer.com"), CancellationToken.None);
        Assert.Equal("http://lan.pushdeer.com/message/push", handler.Uri!.ToString());
    }
}

public class PushDeerConfigValidatorTests
{
    private static PushDeerConfigValidator V(Dictionary<string, string>? options = null) => new(new FakeOptionsShim(options));

    private sealed class FakeOptionsShim : ISystemOptionService
    {
        private readonly Dictionary<string, string> _values;
        public FakeOptionsShim(Dictionary<string, string>? values = null) => _values = values ?? new();
        public string Get(string key, string fallback = "") => _values.TryGetValue(key, out var v) ? v : fallback;
        public bool GetBool(string key, bool fallback = false) =>
            _values.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : fallback;
        public int GetInt(string key, int fallback = 0) =>
            _values.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : fallback;
        public IReadOnlyDictionary<string, string> Snapshot() => _values;
        public Task InitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(string key, string value, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public void Empty_pushkey_rejected() =>
        Assert.Throws<BusinessException>(() => V().Validate(new Channel { Type = ChannelType.PushDeer, Secret = "" }));

    [Theory]
    [InlineData("text")]
    [InlineData("markdown")]
    [InlineData("")]
    public void Valid_type_passes(string other) =>
        V().Validate(new Channel { Type = ChannelType.PushDeer, Secret = "K", Other = other });

    [Fact]
    public void Invalid_type_rejected()
    {
        var ex = Assert.Throws<BusinessException>(() => V().Validate(new Channel { Type = ChannelType.PushDeer, Secret = "K", Other = "image" }));
        Assert.Contains("text 或 markdown", ex.Message);
    }

    [Fact]
    public void Plain_http_url_rejected_without_flag() =>
        Assert.Throws<BusinessException>(() => V().Validate(new Channel { Type = ChannelType.PushDeer, Secret = "K", Url = "http://lan.pushdeer.com" }));

    [Fact]
    public void Plain_http_url_allowed_with_flag() =>
        V(new() { ["ChannelUrlAllowNonHttps"] = "true" })
            .Validate(new Channel { Type = ChannelType.PushDeer, Secret = "K", Url = "http://lan.pushdeer.com" });
}
