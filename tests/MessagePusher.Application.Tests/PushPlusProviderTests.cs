using System.Net;
using System.Text;
using System.Text.Json;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Channels;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Tests;

public class PushPlusProviderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? ContentType { get; private set; }
        public string Body { get; private set; } = "";
        public int Calls { get; private set; }
        private readonly Func<HttpResponseMessage> _responder;
        public CapturingHandler(Func<HttpResponseMessage>? responder = null) =>
            _responder = responder ?? (() => Ok("{\"code\":200,\"msg\":\"请求成功\",\"data\":\"abc\"}"));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri;
            Method = request.Method;
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

    private static (PushPlusProvider Provider, CapturingHandler Handler) Create(Func<HttpResponseMessage>? responder = null)
    {
        var handler = new CapturingHandler(responder);
        return (new PushPlusProvider(new FakeHttpFactory(handler)), handler);
    }

    private static User User() => new() { Id = 1 };
    private static Channel Ch(string accountId = "", string other = "", string secret = "PP_TOKEN") =>
        new() { Type = ChannelType.PushPlus, Secret = secret, AccountId = accountId, Other = other };

    private static Dictionary<string, JsonElement> Props(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task Posts_json_to_fixed_endpoint_with_exact_field_names()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://www.pushplus.plus/send", handler.Uri!.ToString());
        Assert.Equal("application/json", handler.ContentType);

        var props = Props(handler.Body);
        Assert.Equal("PP_TOKEN", props["token"].GetString());
        Assert.Equal("T", props["title"].GetString());
        Assert.Equal("D", props["content"].GetString());
        // 精确协议字段名，无 snake_case 错名；未配置的可选字段不下发。
        Assert.DoesNotContain("app_id", handler.Body);
        Assert.False(props.ContainsKey("topic"));
        Assert.False(props.ContainsKey("template"));
    }

    [Fact]
    public async Task Topic_uses_accountid_and_to_overrides()
    {
        var (p1, h1) = Create();
        await p1.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(accountId: "grp1"), CancellationToken.None);
        Assert.Equal("grp1", Props(h1.Body)["topic"].GetString());

        var (p2, h2) = Create();
        await p2.SendAsync(new Message { Title = "T", Description = "D", To = "grp2" }, User(), Ch(accountId: "grp1"), CancellationToken.None);
        Assert.Equal("grp2", Props(h2.Body)["topic"].GetString());
    }

    [Fact]
    public async Task Template_sent_only_when_configured()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(other: "markdown"), CancellationToken.None);
        Assert.Equal("markdown", Props(handler.Body)["template"].GetString());
    }

    [Fact]
    public async Task Content_falls_back_and_appends_url()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Content = "", Description = "描述", Url = "https://e.com/p" },
            User(), Ch(), CancellationToken.None);
        Assert.Equal("描述\n\nhttps://e.com/p", Props(handler.Body)["content"].GetString());
    }

    [Fact]
    public async Task Title_and_content_truncate_to_documented_limits()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = new string('a', 130), Content = new string('b', 25000) },
            User(), Ch(), CancellationToken.None);
        var props = Props(handler.Body);
        Assert.Equal(100, props["title"].GetString()!.Length);
        Assert.Equal(20000, props["content"].GetString()!.Length);
    }

    [Fact]
    public async Task Truncation_does_not_split_surrogate_pair()
    {
        var (provider, handler) = Create();
        var title = new string('a', 99) + "😀"; // 99 + 2 = 101
        await provider.SendAsync(new Message { Title = title }, User(), Ch(), CancellationToken.None);
        Assert.Equal(new string('a', 99), Props(handler.Body)["title"].GetString());
    }

    [Fact]
    public async Task Business_code_905_is_failure_and_surfaces_msg()
    {
        var (provider, _) = Create(() => CapturingHandler.Ok("{\"code\":905,\"msg\":\"请先完成实名\"}"));
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(), CancellationToken.None));
        Assert.Contains("905", ex.Message);
        Assert.Contains("请先完成实名", ex.Message);
    }

    [Fact]
    public async Task Empty_body_is_failure()
    {
        var (provider, _) = Create(() => CapturingHandler.Ok(""));
        await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(), CancellationToken.None));
    }

    [Fact]
    public async Task Non_2xx_html_redacts_token()
    {
        var (provider, _) = Create(() => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("<html>denied PP_TOKEN</html>", Encoding.UTF8, "text/html")
        });
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(), CancellationToken.None));
        Assert.DoesNotContain("PP_TOKEN", ex.Message);
        Assert.Contains("***", ex.Message);
    }

    [Fact]
    public async Task Input_message_is_not_mutated()
    {
        var (provider, _) = Create();
        var msg = new Message { Title = new string('a', 130), Content = "", Description = "D" };
        await provider.SendAsync(msg, User(), Ch(), CancellationToken.None);
        Assert.Equal(130, msg.Title.Length);
    }
}

public class PushPlusConfigValidatorTests
{
    private readonly PushPlusConfigValidator _v = new();

    [Fact]
    public void Empty_token_rejected() =>
        Assert.Throws<BusinessException>(() => _v.Validate(new Channel { Type = ChannelType.PushPlus, Secret = "" }));

    [Theory]
    [InlineData("")]
    [InlineData("html")]
    [InlineData("markdown")]
    [InlineData("cloudMonitor")]
    public void Valid_template_passes(string template) =>
        _v.Validate(new Channel { Type = ChannelType.PushPlus, Secret = "T", Other = template });

    [Theory]
    [InlineData("<script>")]
    [InlineData("a b")]
    [InlineData("1abc")]
    public void Invalid_template_rejected(string template) =>
        Assert.Throws<BusinessException>(() => _v.Validate(new Channel { Type = ChannelType.PushPlus, Secret = "T", Other = template }));
}
