using System.Net;
using System.Text;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Channels;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Tests;

public class ServerChanProviderTests
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
            _responder = responder ?? (() => Ok("{\"code\":0,\"message\":\"ok\"}"));

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

    private static (ServerChanProvider Provider, CapturingHandler Handler) Create(Func<HttpResponseMessage>? responder = null)
    {
        var handler = new CapturingHandler(responder);
        return (new ServerChanProvider(new FakeHttpFactory(handler)), handler);
    }

    private static Dictionary<string, string> ParseForm(string body) =>
        body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(a => WebUtility.UrlDecode(a[0]), a => a.Length > 1 ? WebUtility.UrlDecode(a[1]) : "");

    private static User User() => new() { Id = 1 };
    private static Channel Turbo(string other = "", string accountId = "") =>
        new() { Type = ChannelType.ServerChan, Secret = "SCT123456tABCDEF", Other = other, AccountId = accountId };
    private static Channel Sc3(string other = "", string accountId = "") =>
        new() { Type = ChannelType.ServerChan, Secret = "sctp98765tXYZTOKEN", Other = other, AccountId = accountId };

    [Fact]
    public async Task Turbo_key_routes_to_sctapi_endpoint_as_form_post()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Turbo(), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://sctapi.ftqq.com/SCT123456tABCDEF.send", handler.Uri!.ToString());
        Assert.Equal("application/x-www-form-urlencoded", handler.ContentType);
    }

    [Fact]
    public async Task Sc3_key_routes_to_uid_subdomain_endpoint()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Description = "D" }, User(), Sc3(), CancellationToken.None);

        Assert.Equal("https://98765.push.ft07.com/send/sctp98765tXYZTOKEN.send", handler.Uri!.ToString());
    }

    [Theory]
    [InlineData("sctpABCDtXYZ")] // SC3 前缀但 uid 非数字
    [InlineData("randomkey")]    // 既非 SCT 也非 sctp
    [InlineData("")]
    public async Task Invalid_sendkey_errors_before_any_request(string key)
    {
        var (provider, handler) = Create();
        var channel = new Channel { Type = ChannelType.ServerChan, Secret = key };
        await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), channel, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Empty_content_falls_back_to_description_and_appends_url_once()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "标题", Content = "", Description = "描述", Url = "https://e.com/a" },
            User(), Turbo(), CancellationToken.None);

        var form = ParseForm(handler.Body);
        Assert.Equal("标题", form["title"]);
        Assert.Equal("描述\n\nhttps://e.com/a", form["desp"]);
    }

    [Fact]
    public async Task Non_empty_content_is_used_and_url_not_duplicated_when_present()
    {
        var (provider, handler) = Create();
        await provider.SendAsync(new Message { Title = "T", Content = "见 https://e.com/a 详情", Description = "忽略", Url = "https://e.com/a" },
            User(), Turbo(), CancellationToken.None);

        Assert.Equal("见 https://e.com/a 详情", ParseForm(handler.Body)["desp"]);
    }

    [Fact]
    public async Task Turbo_title_truncates_at_32_without_splitting_surrogate_pair()
    {
        var (provider, handler) = Create();
        var title = new string('a', 31) + "😀"; // 31 + 2 (代理项) = 33 UTF-16
        await provider.SendAsync(new Message { Title = title }, User(), Turbo(), CancellationToken.None);

        var sent = ParseForm(handler.Body)["title"];
        Assert.Equal(new string('a', 31), sent);
        Assert.True(sent.Length <= 32);
    }

    [Fact]
    public async Task Turbo_uses_accountid_as_channel_and_to_overrides_it()
    {
        var (p1, h1) = Create();
        await p1.SendAsync(new Message { Title = "T" }, User(), Turbo(accountId: "wechat"), CancellationToken.None);
        Assert.Equal("wechat", ParseForm(h1.Body)["channel"]);

        var (p2, h2) = Create();
        await p2.SendAsync(new Message { Title = "T", To = "wxcptest" }, User(), Turbo(accountId: "wechat"), CancellationToken.None);
        Assert.Equal("wxcptest", ParseForm(h2.Body)["channel"]);
    }

    [Fact]
    public async Task Sc3_rejects_non_empty_to_before_request()
    {
        var (provider, handler) = Create();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T", To = "someone" }, User(), Sc3(), CancellationToken.None));
        Assert.Contains("固定目标", ex.Message);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Short_applies_to_both_versions_but_tags_only_to_sc3()
    {
        var (pt, ht) = Create();
        await pt.SendAsync(new Message { Title = "T", Description = "D" }, User(),
            Turbo(other: "{\"short\":\"S\",\"tags\":\"a|b\"}"), CancellationToken.None);
        var turboForm = ParseForm(ht.Body);
        Assert.Equal("S", turboForm["short"]);
        Assert.False(turboForm.ContainsKey("tags"));

        var (ps, hs) = Create();
        await ps.SendAsync(new Message { Title = "T", Description = "D" }, User(),
            Sc3(other: "{\"short\":\"S\",\"tags\":\"a|b\"}"), CancellationToken.None);
        var sc3Form = ParseForm(hs.Body);
        Assert.Equal("S", sc3Form["short"]);
        Assert.Equal("a|b", sc3Form["tags"]);
    }

    [Fact]
    public async Task Business_failure_code_throws_with_server_message()
    {
        var (provider, _) = Create(() => CapturingHandler.Ok("{\"code\":40001,\"message\":\"bad sendkey\"}"));
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Turbo(), CancellationToken.None));
        Assert.Contains("bad sendkey", ex.Message);
    }

    [Fact]
    public async Task Empty_body_is_treated_as_failure()
    {
        var (provider, _) = Create(() => CapturingHandler.Ok(""));
        await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Turbo(), CancellationToken.None));
    }

    [Fact]
    public async Task Non_2xx_html_error_becomes_readable_and_redacts_sendkey()
    {
        var (provider, _) = Create(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html><body>failed for SCT123456tABCDEF</body></html>", Encoding.UTF8, "text/html")
        });
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(new Message { Title = "T" }, User(), Turbo(), CancellationToken.None));
        Assert.Contains("Server酱", ex.Message);
        Assert.DoesNotContain("SCT123456tABCDEF", ex.Message);
        Assert.Contains("***", ex.Message);
    }

    [Fact]
    public async Task Input_message_is_not_mutated()
    {
        var (provider, _) = Create();
        var msg = new Message { Title = new string('a', 40), Content = "", Description = "D", Url = "https://e.com" };
        await provider.SendAsync(msg, User(), Turbo(), CancellationToken.None);
        Assert.Equal(new string('a', 40), msg.Title);
        Assert.Equal("", msg.Content);
    }
}

public class ServerChanConfigValidatorTests
{
    private readonly ServerChanConfigValidator _v = new();

    [Theory]
    [InlineData("SCT123456tABCDEF")]
    [InlineData("sctp98765tXYZTOKEN")]
    public void Valid_keys_pass(string key) =>
        _v.Validate(new Channel { Type = ChannelType.ServerChan, Secret = key });

    [Theory]
    [InlineData("")]
    [InlineData("randomkey")]
    [InlineData("sctpABCDtXYZ")]
    public void Invalid_keys_rejected(string key) =>
        Assert.Throws<BusinessException>(() => _v.Validate(new Channel { Type = ChannelType.ServerChan, Secret = key }));

    [Fact]
    public void Sc3_with_accountid_rejected()
    {
        var ex = Assert.Throws<BusinessException>(() =>
            _v.Validate(new Channel { Type = ChannelType.ServerChan, Secret = "sctp98765tXYZ", AccountId = "wechat" }));
        Assert.Contains("AccountId", ex.Message);
    }

    [Fact]
    public void Turbo_with_tags_option_rejected()
    {
        var ex = Assert.Throws<BusinessException>(() =>
            _v.Validate(new Channel { Type = ChannelType.ServerChan, Secret = "SCT1t", Other = "{\"tags\":\"a\"}" }));
        Assert.Contains("tags", ex.Message);
    }

    [Fact]
    public void Malformed_other_json_rejected() =>
        Assert.Throws<BusinessException>(() =>
            _v.Validate(new Channel { Type = ChannelType.ServerChan, Secret = "SCT1t", Other = "not-json" }));
}
