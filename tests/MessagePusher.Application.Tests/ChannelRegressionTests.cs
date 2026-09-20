using System.Collections.Concurrent;
using System.Net;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Channels;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MessagePusher.Application.Tests;

public class ChannelRegressionTests
{
    private const string Secret = "SCT_REVIEW_FAKE_SECRET";

    [Theory]
    [InlineData("server_chan")]
    [InlineData("pushdeer")]
    [InlineData("push_plus")]
    [InlineData("ntfy")]
    [InlineData("gotify")]
    [InlineData("pushover")]
    [InlineData("wx_pusher")]
    public async Task Invalid_json_is_a_readable_business_error(string type)
    {
        foreach (var body in new[] { "not-json " + Secret, "[]", "null" })
        {
            var ex = await Assert.ThrowsAsync<BusinessException>(() => Send(type, body));
            Assert.DoesNotContain(Secret, ex.ToString());
            Assert.Contains("响应", ex.Message);
        }
    }

    [Theory]
    [InlineData("server_chan", "{\"code\":1,\"message\":\"invalid SCT_REVIEW_FAKE_SECRET\"}")]
    [InlineData("pushdeer", "{\"code\":\"9999\",\"error\":\"invalid SCT_REVIEW_FAKE_SECRET\"}")]
    [InlineData("push_plus", "{\"code\":500,\"msg\":\"invalid SCT_REVIEW_FAKE_SECRET\"}")]
    [InlineData("ntfy", "{\"error\":\"invalid SCT_REVIEW_FAKE_SECRET\"}")]
    [InlineData("gotify", "{\"errorDescription\":\"invalid SCT_REVIEW_FAKE_SECRET\"}")]
    [InlineData("pushover", "{\"status\":0,\"errors\":[\"invalid SCT_REVIEW_FAKE_SECRET\"]}")]
    [InlineData("wx_pusher", "{\"code\":1001,\"msg\":\"invalid SCT_REVIEW_FAKE_SECRET\"}")]
    [InlineData("wx_pusher", "{\"code\":1000,\"data\":[{\"uid\":\"UID_1\",\"code\":1001,\"status\":\"invalid SCT_REVIEW_FAKE_SECRET\"}]}")]
    public async Task Business_errors_never_echo_credentials(string type, string response)
    {
        var ex = await Assert.ThrowsAsync<BusinessException>(() => Send(type, response));
        Assert.DoesNotContain(Secret, ex.ToString());
        Assert.Contains("***", ex.Message);
    }

    [Theory]
    [InlineData("server_chan", "{}")]
    [InlineData("server_chan", "{\"message\":\"failure\"}")]
    [InlineData("server_chan", "{\"code\":null}")]
    [InlineData("ntfy", "{\"event\":\"message\"}")]
    [InlineData("ntfy", "{\"id\":\"abc\"}")]
    [InlineData("ntfy", "{\"id\":\"abc\",\"event\":\"keepalive\"}")]
    [InlineData("ntfy", "{\"id\":\"abc\",\"event\":\"message\",\"error\":\"denied\"}")]
    [InlineData("wx_pusher", "{\"code\":1000}")]
    [InlineData("wx_pusher", "{\"code\":1000,\"data\":[]}")]
    [InlineData("wx_pusher", "{\"code\":1000,\"data\":{\"fails\":[]}}")]
    [InlineData("wx_pusher", "{\"code\":1000,\"data\":[{\"uid\":\"UID_1\"}]}")]
    [InlineData("wx_pusher", "{\"code\":1000,\"data\":[null]}")]
    public async Task Incomplete_success_responses_are_rejected(string type, string response) =>
        await Assert.ThrowsAsync<BusinessException>(() => Send(type, response));

    [Fact]
    public async Task WxPusher_checks_each_uid_and_topic_result()
    {
        await Send("wx_pusher", """{"code":1000,"success":true,"data":[{"uid":"UID_1","code":1000,"status":"accepted"},{"topicId":23,"code":1000,"status":"accepted"}]}""");
        var ex = await Assert.ThrowsAsync<BusinessException>(() => Send("wx_pusher",
            """{"code":1000,"success":true,"data":[{"uid":"UID_1","code":1000},{"topicId":23,"code":1001,"status":"denied"}]}"""));
        Assert.Contains("23", ex.Message);
        Assert.Contains("denied", ex.Message);
    }

    [Fact]
    public async Task Configured_http_pipeline_does_not_log_path_credentials()
    {
        var logs = new RecordingLogs();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddApplication();
        services.AddHttpClient("channels").ConfigurePrimaryHttpMessageHandler(() => new ReplyHandler("{\"code\":0}"));
        using var sp = services.BuildServiceProvider();
        var http = sp.GetRequiredService<IHttpClientFactory>();
        await new ServerChanProvider(http).SendAsync(new Message { Title = "test" }, new User(),
            new Channel { Secret = Secret }, CancellationToken.None);
        Assert.DoesNotContain(logs.Lines, line => line.Contains(Secret, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://user:password@node.example/")]
    [InlineData("https://user@node.example/")]
    public void Url_credentials_are_rejected(string url) =>
        Assert.Throws<BusinessException>(() => OutboundUrlPolicy.Validate(url, new Options(), "test"));

    [Theory]
    [InlineData("https://push.example.com/", "https://push.example.com:443/send")]
    [InlineData("https://push.example.com:443/", "https://PUSH.EXAMPLE.COM/send")]
    [InlineData("https://push.example.com/", "https://push.example.com./send")]
    [InlineData("https://push.example.com/base/", "https://push.example.com:443/base/send")]
    public void Self_address_is_compared_as_a_normalized_uri(string server, string target) =>
        Assert.Throws<BusinessException>(() => OutboundUrlPolicy.Validate(target, new Options(server), "test"));

    [Theory]
    [InlineData("https://push.example.com", "https://push.example.com.evil.test/send")]
    [InlineData("https://push.example.com/base", "https://push.example.com/base-other/send")]
    [InlineData("https://push.example.com", "https://push.example.com:8443/send")]
    public void Different_origins_or_base_paths_are_not_mistaken_for_self(string server, string target) =>
        OutboundUrlPolicy.Validate(target, new Options(server), "test");

    [Theory]
    [InlineData("123")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("true")]
    public void Wrong_option_types_are_rejected(string value)
    {
        Assert.Throws<BusinessException>(() => new NtfyConfigValidator(new Options()).Validate(
            new Channel { AccountId = "topic", Other = "{\"tags\":" + value + "}" }));
        Assert.Throws<BusinessException>(() => new WxPusherConfigValidator().Validate(
            new Channel { Secret = Secret, AccountId = "UID_1", Other = "{\"mode\":" + value + "}" }));
    }

    [Fact]
    public void Ntfy_valid_arrays_ignore_blank_tags_but_reject_non_strings()
    {
        var parsed = NtfyOptions.Parse("""{"tags":["warning"," "," ops "]}""");
        Assert.Equal(new[] { "warning", "ops" }, parsed.Tags);
        Assert.Throws<BusinessException>(() => NtfyOptions.Parse("""{"tags":["warning",123]}"""));
    }

    private static Task Send(string type, string body)
    {
        var http = new ReplyFactory(body);
        var options = new Options();
        IChannelProvider provider = type switch
        {
            "server_chan" => new ServerChanProvider(http),
            "pushdeer" => new PushDeerProvider(http, options),
            "push_plus" => new PushPlusProvider(http),
            "ntfy" => new NtfyProvider(http, options),
            "gotify" => new GotifyProvider(http, options),
            "pushover" => new PushoverProvider(http),
            "wx_pusher" => new WxPusherProvider(http),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        return provider.SendAsync(new Message { Title = "test", Content = "body" }, new User(),
            new Channel { Type = type, Secret = Secret, AccountId = "UID_1", Url = "https://node.example" }, CancellationToken.None);
    }

    private sealed class ReplyFactory(string body) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ReplyHandler(body));
    }

    private sealed class ReplyHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    private sealed class Options(string server = "https://push.example.com") : ISystemOptionService
    {
        public string Get(string key, string fallback = "") => key == "ServerAddress" ? server : fallback;
        public bool GetBool(string key, bool fallback = false) => fallback;
        public int GetInt(string key, int fallback = 0) => fallback;
        public IReadOnlyDictionary<string, string> Snapshot() => new Dictionary<string, string>();
        public Task InitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(string key, string value, CancellationToken ct = default) => Task.CompletedTask;
    }
}

internal sealed class RecordingLogs : ILoggerProvider
{
    public ConcurrentQueue<string> Lines { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Sink(this);
    public void Dispose() { }
    private sealed class Sink(RecordingLogs owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> format) =>
            owner.Lines.Enqueue(format(state, ex));
    }
}
