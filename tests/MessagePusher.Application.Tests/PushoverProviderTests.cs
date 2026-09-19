using System.Net;
using System.Text;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Channels;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Tests;

public class PushoverProviderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? ContentType { get; private set; }
        public string Body { get; private set; } = "";
        public int Calls { get; private set; }
        private readonly Func<HttpResponseMessage> _responder;
        public CapturingHandler(Func<HttpResponseMessage>? responder = null) =>
            _responder = responder ?? (() => Ok("{\"status\":1,\"request\":\"req-id\"}"));

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

    private static (PushoverProvider Provider, CapturingHandler Handler) Create(Func<HttpResponseMessage>? responder = null)
    {
        var handler = new CapturingHandler(responder);
        return (new PushoverProvider(new FakeHttpFactory(handler)), handler);
    }

    private static Dictionary<string, string> ParseForm(string body) =>
        body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(a => WebUtility.UrlDecode(a[0]), a => a.Length > 1 ? WebUtility.UrlDecode(a[1]) : "");

    private static User User() => new() { Id = 1 };
    private static Channel Ch(string secret = "APP_TOKEN", string accountId = "USER_KEY") =>
        new() { Type = ChannelType.Pushover, Secret = secret, AccountId = accountId };

    [Fact]
    public async Task Posts_form_to_fixed_endpoint()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Ch(), CancellationToken.None);
        Assert.Equal("https://api.pushover.net/1/messages.json", handler.Uri!.ToString());
        Assert.Equal("application/x-www-form-urlencoded", handler.ContentType);
        var form = ParseForm(handler.Body);
        Assert.Equal("APP_TOKEN", form["token"]);
        Assert.Equal("USER_KEY", form["user"]);
        Assert.Equal("D", form["message"]);
        Assert.Equal("T", form["title"]);
    }

    [Fact]
    public async Task To_overrides_user_key()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", To = "OTHER_USER" }, User(), Ch(), CancellationToken.None);
        Assert.Equal("OTHER_USER", ParseForm(handler.Body)["user"]);
    }

    [Fact]
    public async Task Empty_user_key_throws_before_request()
    {
        var (provider, handler) = Create();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(accountId: ""), CancellationToken.None));
        Assert.Contains("user", ex.Message);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Message_falls_back_to_description_and_url_is_separate_field()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Content = "", Description = "描述", Url = "https://e.com/x" },
            User(), Ch(), CancellationToken.None);
        var form = ParseForm(handler.Body);
        Assert.Equal("描述", form["message"]);
        Assert.Equal("https://e.com/x", form["url"]);
    }

    [Fact]
    public async Task Title_and_url_omitted_when_empty()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "", Description = "D" }, User(), Ch(), CancellationToken.None);
        var form = ParseForm(handler.Body);
        Assert.False(form.ContainsKey("title"));
        Assert.False(form.ContainsKey("url"));
    }

    [Fact]
    public async Task Long_message_truncated_to_1024()
    {
        var (provider, handler) = Create();
        var longText = new string('a', 2000);
        await provider.SendAsync(new Message { Title = "T", Description = longText }, User(), Ch(), CancellationToken.None);
        Assert.Equal(1024, ParseForm(handler.Body)["message"].Length);
    }

    [Fact]
    public async Task Chinese_form_encoded_correctly()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "标题 空格&符号=测试", Description = "内容" }, User(), Ch(), CancellationToken.None);
        Assert.Equal("标题 空格&符号=测试", ParseForm(handler.Body)["title"]);
    }

    [Fact]
    public async Task Status_not_one_is_failure_and_surfaces_errors()
    {
        var (provider, _) = Create(() => CapturingHandler.Ok("{\"status\":0,\"errors\":[\"application token is not valid\"]}"));
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(), CancellationToken.None));
        Assert.Contains("application token is not valid", ex.Message);
    }

    [Fact]
    public async Task Empty_body_is_failure()
    {
        var (provider, _) = Create(() => CapturingHandler.Ok(""));
        await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(), CancellationToken.None));
    }

    [Fact]
    public async Task Non_2xx_surfaces_readable_error_and_redacts()
    {
        var (provider, _) = Create(() => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"errors\":[\"user key APP_TOKEN invalid\"]}", Encoding.UTF8, "application/json")
        });
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Ch(), CancellationToken.None));
        Assert.Contains("Pushover", ex.Message);
        Assert.DoesNotContain("APP_TOKEN", ex.Message);
    }
}

public class PushoverConfigValidatorTests
{
    private static PushoverConfigValidator V() => new();

    [Fact]
    public void Empty_token_rejected() =>
        Assert.Throws<BusinessException>(() => V().Validate(new Channel { Type = ChannelType.Pushover, Secret = "", AccountId = "U" }));

    [Fact]
    public void Empty_user_key_rejected() =>
        Assert.Throws<BusinessException>(() => V().Validate(new Channel { Type = ChannelType.Pushover, Secret = "T", AccountId = "" }));

    [Fact]
    public void Valid_config_passes() =>
        V().Validate(new Channel { Type = ChannelType.Pushover, Secret = "T", AccountId = "U" });
}
