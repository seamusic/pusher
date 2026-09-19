using System.Diagnostics;
using System.Net;
using System.Text;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Channels;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Tests;

public class HttpChannelHelpersTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
            _responder = responder;

        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];
        public int Calls => Requests.Count;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            RequestBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return await _responder(request, cancellationToken);
        }
    }

    private sealed class ThrowingStream : Stream
    {
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("read fail");
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => throw new IOException("read fail");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
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

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task PostFormAsync_encodes_chinese_and_special_chars()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{\"code\":0}")));
        using var client = new HttpClient(handler);

        var fields = new[]
        {
            new KeyValuePair<string, string>("title", "中文标题"),
            new KeyValuePair<string, string>("desp", "a&b=c+d 空格#hash"),
        };
        var (resp, body) = await HttpChannelHelpers.PostFormAsync(client, "https://example.com/api", fields, CancellationToken.None);
        using (resp)
        {
            Assert.Equal("{\"code\":0}", body);
            var sent = handler.RequestBodies[0];
            Assert.Contains("%E4%B8%AD%E6%96%87", sent, StringComparison.Ordinal);
            Assert.DoesNotContain("中文", sent);
            Assert.Equal("application/x-www-form-urlencoded", handler.Requests[0].Content!.Headers.ContentType!.MediaType);
        }
    }

    [Fact]
    public async Task PostFormAsync_passes_custom_headers()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "ok")));
        using var client = new HttpClient(handler);

        var (resp, _) = await HttpChannelHelpers.PostFormAsync(client, "https://example.com/api",
            [new KeyValuePair<string, string>("k", "v")], CancellationToken.None,
            new Dictionary<string, string> { ["Authorization"] = "Bearer tok" });
        using (resp);
        Assert.Equal("Bearer tok", handler.Requests[0].Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task PostRawAsync_disposes_response_when_body_read_fails()
    {
        var stream = new ThrowingStream();
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            HttpChannelHelpers.PostRawAsync(client, "https://example.com", new { a = 1 }, CancellationToken.None));
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task SendAndReadAsync_non2xx_html_becomes_readable_error()
    {
        var html = "<html><head><title>502 Bad Gateway</title></head><body><h1>502 Bad Gateway</h1><p>nginx</p></body></html>";
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            ReasonPhrase = "Bad Gateway",
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        }));
        using var client = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            HttpChannelHelpers.SendAndReadAsync(client, HttpMethod.Post, "https://example.com/send",
                new StringContent("{}"), "测试通道", CancellationToken.None));
        Assert.Contains("HTTP 502", ex.Message);
        Assert.Contains("测试通道", ex.Message);
        Assert.DoesNotContain("<html", ex.Message);
        Assert.DoesNotContain("<h1>", ex.Message);
    }

    [Fact]
    public async Task SendAndReadAsync_error_message_redacts_secrets_and_truncates()
    {
        var longBody = new string('x', 500) + " pushkey=SCTsecret123";
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(longBody)
        }));
        using var client = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            HttpChannelHelpers.SendAndReadAsync(client, HttpMethod.Post, "https://example.com/send",
                new StringContent("{}"), "测试通道", CancellationToken.None, sensitiveValues: ["SCTsecret123"]));
        Assert.DoesNotContain("SCTsecret123", ex.Message);
        Assert.True(ex.Message.Length < 300, $"error message too long: {ex.Message.Length}");
    }

    [Fact]
    public async Task SendAndReadAsync_precancelled_token_terminates()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "ok")));
        using var client = new HttpClient(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            HttpChannelHelpers.SendAndReadAsync(client, HttpMethod.Post, "https://example.com/send",
                new StringContent("{}"), "测试通道", cts.Token));
    }

    [Fact]
    public async Task SendAndReadAsync_timeout_terminates_wait()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return Json(HttpStatusCode.OK, "ok");
        });
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        var sw = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            HttpChannelHelpers.SendAndReadAsync(client, HttpMethod.Post, "https://example.com/send",
                new StringContent("{}"), "测试通道", CancellationToken.None));
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"timeout not enforced, elapsed {sw.Elapsed}");
    }

    [Fact]
    public async Task SendAndReadAsync_does_not_retry_on_server_error()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.TooManyRequests, "{\"error\":\"rate limited\"}")));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<BusinessException>(() =>
            HttpChannelHelpers.SendAndReadAsync(client, HttpMethod.Post, "https://example.com/send",
                new StringContent("{}"), "测试通道", CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task SendAndReadAsync_success_returns_body_and_disposes_response()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{\"code\":0,\"data\":{}}")));
        using var client = new HttpClient(handler);

        var body = await HttpChannelHelpers.SendAndReadAsync(client, HttpMethod.Post, "https://example.com/send",
            new StringContent("{}", Encoding.UTF8, "application/json"), "测试通道", CancellationToken.None);
        Assert.Equal("{\"code\":0,\"data\":{}}", body);
    }

    [Fact]
    public void OutboundUrlPolicy_requires_https_by_default()
    {
        var options = new FakeOptions();
        OutboundUrlPolicy.Validate("https://example.com/api", options, "测试通道");
        Assert.Throws<BusinessException>(() => OutboundUrlPolicy.Validate("http://example.com/api", options, "测试通道"));
        Assert.Throws<BusinessException>(() => OutboundUrlPolicy.Validate("", options, "测试通道"));
        Assert.Throws<BusinessException>(() => OutboundUrlPolicy.Validate("ftp://example.com", options, "测试通道"));
        Assert.Throws<BusinessException>(() => OutboundUrlPolicy.Validate("not-a-url", options, "测试通道"));
    }

    [Fact]
    public void OutboundUrlPolicy_allows_http_only_with_explicit_switch()
    {
        var options = new FakeOptions(new Dictionary<string, string> { ["ChannelUrlAllowNonHttps"] = "true" });
        OutboundUrlPolicy.Validate("http://192.168.1.10:8080/api", options, "测试通道");

        var disabled = new FakeOptions(new Dictionary<string, string> { ["ChannelUrlAllowNonHttps"] = "false" });
        Assert.Throws<BusinessException>(() => OutboundUrlPolicy.Validate("http://192.168.1.10:8080/api", disabled, "测试通道"));
    }

    [Fact]
    public void OutboundUrlPolicy_rejects_self_address()
    {
        var options = new FakeOptions(new Dictionary<string, string>
        {
            ["ServerAddress"] = "https://push.example.com",
            ["ChannelUrlAllowNonHttps"] = "true"
        });
        Assert.Throws<BusinessException>(() =>
            OutboundUrlPolicy.Validate("https://push.example.com/api/push", options, "测试通道"));
        OutboundUrlPolicy.Validate("https://other.example.com/api", options, "测试通道");
    }

    [Fact]
    public void OutboundUrlPolicy_fills_default_server_for_empty_url()
    {
        Assert.Equal("https://api2.pushdeer.com", OutboundUrlPolicy.WithDefault("", "https://api2.pushdeer.com"));
        Assert.Equal("https://api2.pushdeer.com", OutboundUrlPolicy.WithDefault("  ", "https://api2.pushdeer.com"));
        Assert.Equal("https://self.host", OutboundUrlPolicy.WithDefault("https://self.host/", "https://api2.pushdeer.com"));
        Assert.Equal("https://self.host/api", OutboundUrlPolicy.WithDefault("https://self.host/api", "https://api2.pushdeer.com"));
    }

    [Fact]
    public void SecretMask_redacts_values_and_masks_keys()
    {
        Assert.Equal("url *** path", SecretMask.Redact("url SCTabc123 path", "SCTabc123"));
        Assert.Equal("no secrets", SecretMask.Redact("no secrets", "", null));
        Assert.Equal("***", SecretMask.MaskKey("short"));
        Assert.Equal("SCTa***", SecretMask.MaskKey("SCTabcdefgh"));
        Assert.Equal("", SecretMask.MaskKey(""));
    }
}
