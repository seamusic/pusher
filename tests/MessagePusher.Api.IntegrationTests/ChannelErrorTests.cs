using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Domain;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MessagePusher.Api.IntegrationTests;

public class ChannelErrorTests
{
    private const string Secret = "SCT_API_FAKE_SECRET";

    [Theory]
    [InlineData("malformed", "push_plus", "响应格式")]
    [InlineData("timeout", "push_plus", "超时")]
    [InlineData("business", "push_plus", "拒绝")]
    [InlineData("missing-code", "server_chan", "发送失败")]
    public async Task Upstream_failures_return_business_errors_without_secrets(string scenario, string type, string expected)
    {
        using var root = new ApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient("channels", client => client.Timeout = TimeSpan.FromMilliseconds(200))
                .ConfigurePrimaryHttpMessageHandler(() => new UpstreamHandler(scenario))));
        using var client = factory.CreateClient();
        await AddChannel(client, type);

        using var response = await client.PostAsJsonAsync("/push/root",
            new { channel = "review_channel", title = "review", content = "stub only" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains(expected, result.GetProperty("message").GetString());
        Assert.DoesNotContain(Secret, result.ToString());
    }

    [Fact]
    public async Task Async_push_is_consumed_and_persists_sent_status()
    {
        var handler = new UpstreamHandler("success");
        using var root = new ApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient("channels").ConfigurePrimaryHttpMessageHandler(() => handler)));
        using var client = factory.CreateClient();
        await AddChannel(client, "push_plus");

        using var response = await client.PostAsJsonAsync("/push/root",
            new { channel = "review_channel", title = "review", content = "stub only", @async = true });
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("success").GetBoolean(), result.ToString());
        var link = result.GetProperty("uuid").GetString()!;
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (await repository.GetStatusByLinkAsync(link, deadline.Token) == (int)MessageSendStatus.AsyncPending)
            await Task.Delay(20, deadline.Token);
        Assert.Equal((int)MessageSendStatus.Sent, await repository.GetStatusByLinkAsync(link, deadline.Token));
        Assert.Equal(1, handler.Calls);
    }

    private static async Task AddChannel(HttpClient client, string type)
    {
        using var login = await client.PostAsJsonAsync("/api/user/login", new { username = "root", password = "123456" });
        var authenticated = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(authenticated.GetProperty("success").GetBoolean(), authenticated.ToString());
        using var create = await client.PostAsJsonAsync("/api/channel",
            new { name = "review_channel", type, secret = Secret });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(created.GetProperty("success").GetBoolean(), created.ToString());
    }

    private sealed class UpstreamHandler(string scenario) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            if (scenario == "timeout") await Task.Delay(Timeout.Infinite, ct);
            var body = scenario switch
            {
                "malformed" => "not-json " + Secret,
                "business" => "{\"code\":500,\"msg\":\"拒绝 " + Secret + "\"}",
                "missing-code" => "{}",
                _ => "{\"code\":200,\"msg\":\"ok\"}"
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
}
