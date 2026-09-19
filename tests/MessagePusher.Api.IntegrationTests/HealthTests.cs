using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MessagePusher.Api.IntegrationTests;

public class HealthTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    public HealthTests(ApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_returns_ok()
    {
        var res = await _client.GetAsync("/health");
        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Ready_returns_ok()
    {
        var res = await _client.GetAsync("/ready");
        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Status_returns_success()
    {
        var res = await _client.GetAsync("/api/status");
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":true", json);
        Assert.Contains("system_name", json);
    }

    [Fact]
    public async Task Root_can_login()
    {
        var res = await _client.PostAsJsonAsync("/api/user/login", new { username = "root", password = "123456" });
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":true", json);
        Assert.Contains("\"username\":\"root\"", json);
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), "mp-test-" + Guid.NewGuid().ToString("N") + ".db");

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureHostConfiguration(c =>
        {
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "sqlite",
                ["Database:SqlitePath"] = _db,
                ["AppSettings:SessionSecret"] = "test-secret",
                ["AppSettings:DataProtectionKeysPath"] = Path.Combine(Path.GetTempPath(), "mp-keys-" + Guid.NewGuid().ToString("N"))
            });
        });
        return base.CreateHost(builder);
    }
}
