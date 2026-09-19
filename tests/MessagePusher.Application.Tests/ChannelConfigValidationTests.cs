using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Application.Channels;
using MessagePusher.Application.Services;
using MessagePusher.Application.Tokens;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MessagePusher.Application.Tests;

public class ChannelConfigValidationTests
{
    private sealed class InMemoryChannelRepository : IChannelRepository
    {
        public Dictionary<int, Channel> Store { get; } = [];
        private int _nextId = 1;

        public Task<Channel?> GetByIdAsync(int id, int userId, bool selectAll, CancellationToken ct = default) =>
            Task.FromResult(Store.TryGetValue(id, out var c) && c.UserId == userId ? Copy(c) : null);

        public Task<Channel?> GetByNameAsync(string name, int userId, CancellationToken ct = default) =>
            Task.FromResult(Store.Values.FirstOrDefault(c => c.Name == name && c.UserId == userId) is { } c ? Copy(c) : null);

        public Task AddAsync(Channel channel, CancellationToken ct = default)
        {
            if (channel.Id == 0)
                channel.Id = _nextId++;
            Store[channel.Id] = Copy(channel);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Channel channel, CancellationToken ct = default)
        {
            Store[channel.Id] = Copy(channel);
            return Task.CompletedTask;
        }

        public Task<Channel?> DeleteAsync(int id, int userId, CancellationToken ct = default)
        {
            if (Store.TryGetValue(id, out var c) && c.UserId == userId)
            {
                Store.Remove(id);
                return Task.FromResult<Channel?>(Copy(c));
            }
            return Task.FromResult<Channel?>(null);
        }

        public Task<IReadOnlyList<Channel>> GetByUserIdAsync(int userId, int offset, int count, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BriefChannel>> GetBriefByUserIdAsync(int userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Channel>> SearchAsync(int userId, string keyword, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Channel>> GetTokenStoreChannelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Channel>>([]);
        public Task<IReadOnlyList<Channel>> GetTokenStoreChannelsByUserIdAsync(int userId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Channel>>([]);
        public Task<int> CountSharedAsync(string type, string appId, string secret, CancellationToken ct = default) => Task.FromResult(1);
        public Task UpdateStatusAsync(Channel channel, int status, CancellationToken ct = default) => throw new NotSupportedException();

        private static Channel Copy(Channel c) => new()
        {
            Id = c.Id, Type = c.Type, UserId = c.UserId, Name = c.Name, Description = c.Description,
            Status = c.Status, Secret = c.Secret, AppId = c.AppId, AccountId = c.AccountId,
            Url = c.Url, Other = c.Other, CreatedTime = c.CreatedTime, Token = c.Token
        };
    }

    private sealed class RecordingTokenStore : ITokenStore
    {
        public List<(string Kind, Channel Channel)> Calls { get; } = [];
        public string GetToken(string key) => "";
        public Task AddChannelAsync(Channel channel, CancellationToken ct = default)
        { Calls.Add(("add", channel)); return Task.CompletedTask; }
        public Task RemoveChannelAsync(Channel channel, CancellationToken ct = default)
        { Calls.Add(("remove", channel)); return Task.CompletedTask; }
        public Task UpdateChannelAsync(Channel newChannel, Channel oldChannel, CancellationToken ct = default)
        { Calls.Add(("update", newChannel)); return Task.CompletedTask; }
        public Task AddUserAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveUserAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
        public long UnixSeconds => 0;
        public long UnixMilliseconds => 0;
    }

    // 测试专用 Type 与校验器：要求 Secret 非空、AccountId 为数字。
    private const string FakeType = "fake_validator_type";

    private sealed class FakeProvider : IChannelProvider
    {
        public string Type => FakeType;
        public Task SendAsync(Message message, User user, Channel channel, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeValidator : IChannelConfigValidator
    {
        public string Type => FakeType;
        public void Validate(Channel channel)
        {
            if (string.IsNullOrEmpty(channel.Secret))
                throw new BusinessException("fake 通道必须提供 Secret");
            if (!string.IsNullOrEmpty(channel.AccountId) && !long.TryParse(channel.AccountId, out _))
                throw new BusinessException("fake 通道 AccountId 必须为数字");
        }
    }

    private static (ChannelService Service, InMemoryChannelRepository Repo, RecordingTokenStore Tokens) CreateService()
    {
        var repo = new InMemoryChannelRepository();
        var tokens = new RecordingTokenStore();
        var factory = new ChannelProviderFactory([new NoneProvider(), new FakeProvider()]);
        var registry = new ChannelConfigValidatorRegistry([new FakeValidator()]);
        return (new ChannelService(repo, tokens, new FixedClock(), factory, registry), repo, tokens);
    }

    private static Channel NewInput(string type = FakeType, string name = "ch", string secret = "S") => new()
    {
        Type = type, Name = name, Secret = secret, AccountId = "123"
    };

    [Fact]
    public async Task Add_unknown_type_is_rejected_and_not_saved()
    {
        var (service, repo, tokens) = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.AddAsync(1, NewInput(type: "slack"), CancellationToken.None));
        Assert.Contains("不支持的通道类型", ex.Message);
        Assert.Empty(repo.Store);
        Assert.Empty(tokens.Calls);
    }

    [Fact]
    public async Task Add_invalid_config_is_rejected_and_not_saved()
    {
        var (service, repo, _) = CreateService();
        await Assert.ThrowsAsync<BusinessException>(() =>
            service.AddAsync(1, NewInput(secret: ""), CancellationToken.None));
        var badAccountId = NewInput();
        badAccountId.AccountId = "abc";
        await Assert.ThrowsAsync<BusinessException>(() =>
            service.AddAsync(1, badAccountId, CancellationToken.None));
        Assert.Empty(repo.Store);
    }

    [Fact]
    public async Task Add_valid_config_is_saved_and_token_store_notified()
    {
        var (service, repo, tokens) = CreateService();
        await service.AddAsync(7, NewInput(), CancellationToken.None);
        var saved = Assert.Single(repo.Store.Values);
        Assert.Equal(7, saved.UserId);
        Assert.Equal("S", saved.Secret);
        Assert.Equal((int)ChannelStatus.Enabled, saved.Status);
        Assert.Equal("add", Assert.Single(tokens.Calls).Kind);
    }

    [Fact]
    public async Task Add_rejects_reserved_and_overlong_names()
    {
        var (service, _, _) = CreateService();
        await Assert.ThrowsAsync<BusinessException>(() =>
            service.AddAsync(1, NewInput(name: "email"), CancellationToken.None));
        await Assert.ThrowsAsync<BusinessException>(() =>
            service.AddAsync(1, NewInput(name: new string('n', 21)), CancellationToken.None));
    }

    [Fact]
    public async Task Update_same_type_empty_secret_retains_old_and_result_hides_secret()
    {
        var (service, repo, _) = CreateService();
        await service.AddAsync(1, NewInput(secret: "old-secret"), CancellationToken.None);
        var id = repo.Store.Keys.Single();

        var result = await service.UpdateAsync(1, new Channel { Id = id, Type = FakeType, Name = "ch", Secret = "", AccountId = "123" }, false, CancellationToken.None);

        Assert.Equal("old-secret", repo.Store[id].Secret);
        Assert.Equal("", result.Secret);
    }

    [Fact]
    public async Task Update_cross_type_does_not_reuse_old_secret()
    {
        var (service, repo, _) = CreateService();
        await service.AddAsync(1, NewInput(secret: "old-secret"), CancellationToken.None);
        var id = repo.Store.Keys.Single();

        await service.UpdateAsync(1, new Channel { Id = id, Type = ChannelType.None, Name = "ch", Secret = "" }, false, CancellationToken.None);

        Assert.Equal(ChannelType.None, repo.Store[id].Type);
        Assert.Equal("", repo.Store[id].Secret);
    }

    [Fact]
    public async Task Update_invalid_config_is_rejected_and_store_unchanged()
    {
        var (service, repo, _) = CreateService();
        await service.AddAsync(1, NewInput(secret: "old-secret"), CancellationToken.None);
        var id = repo.Store.Keys.Single();

        await Assert.ThrowsAsync<BusinessException>(() =>
            service.UpdateAsync(1, new Channel { Id = id, Type = FakeType, Name = "ch", Secret = "S", AccountId = "abc" }, false, CancellationToken.None));

        Assert.Equal("123", repo.Store[id].AccountId);
    }

    [Fact]
    public async Task Update_status_only_requires_no_secret_and_skips_config_validation()
    {
        var (service, repo, _) = CreateService();
        await service.AddAsync(1, NewInput(secret: "old-secret"), CancellationToken.None);
        var id = repo.Store.Keys.Single();

        var result = await service.UpdateAsync(1, new Channel { Id = id, Status = (int)ChannelStatus.Disabled }, true, CancellationToken.None);

        Assert.Equal((int)ChannelStatus.Disabled, repo.Store[id].Status);
        Assert.Equal("old-secret", repo.Store[id].Secret);
        Assert.Equal("", result.Secret);
    }
}

public class ChannelTargetPolicyTests
{
    [Fact]
    public void Dynamic_target_prefers_message_To_over_AccountId()
    {
        var channel = new Channel { AccountId = "configured" };
        Assert.Equal("override", ChannelTargetPolicy.ResolveDynamic(new Message { To = "override" }, channel));
        Assert.Equal("configured", ChannelTargetPolicy.ResolveDynamic(new Message { To = "" }, channel));
    }

    [Fact]
    public void Fixed_target_rejects_non_empty_To_with_readable_error()
    {
        ChannelTargetPolicy.RejectFixedTargetTo(new Message { To = "" }, "PushDeer");
        var ex = Assert.Throws<BusinessException>(() =>
            ChannelTargetPolicy.RejectFixedTargetTo(new Message { To = "someone" }, "PushDeer"));
        Assert.Contains("PushDeer", ex.Message);
        Assert.Contains("固定目标", ex.Message);
    }
}

public class TokenStoreCrossTypeTests
{
    private sealed class FakeHttpFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = request.RequestUri!.Host.Contains("feishu")
                ? "{\"code\":0,\"msg\":\"ok\",\"tenant_access_token\":\"t-feishu\"}"
                : "{\"errcode\":0,\"errmsg\":\"ok\",\"access_token\":\"t-wechat\"}";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _sp;
        public FakeScopeFactory(IServiceProvider sp) => _sp = sp;
        public IServiceScope CreateScope() => new FakeScope(_sp);

        private sealed class FakeScope : IServiceScope
        {
            public FakeScope(IServiceProvider sp) => ServiceProvider = sp;
            public IServiceProvider ServiceProvider { get; }
            public void Dispose() { }
        }
    }

    private sealed class CountingRepo : IChannelRepository
    {
        public Task<int> CountSharedAsync(string type, string appId, string secret, CancellationToken ct = default) => Task.FromResult(1);
        public Task<Channel?> GetByIdAsync(int id, int userId, bool selectAll, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Channel?> GetByNameAsync(string name, int userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Channel>> GetByUserIdAsync(int userId, int offset, int count, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BriefChannel>> GetBriefByUserIdAsync(int userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Channel>> SearchAsync(int userId, string keyword, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Channel>> GetTokenStoreChannelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Channel>>([]);
        public Task<IReadOnlyList<Channel>> GetTokenStoreChannelsByUserIdAsync(int userId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Channel>>([]);
        public Task AddAsync(Channel channel, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Channel channel, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateStatusAsync(Channel channel, int status, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Channel?> DeleteAsync(int id, int userId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _map;
        public FakeServiceProvider(Dictionary<Type, object> map) => _map = map;
        public object? GetService(Type serviceType) => _map.TryGetValue(serviceType, out var s) ? s : null;
    }

    private static TokenStore CreateStore()
    {
        var sp = new FakeServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IChannelRepository)] = new CountingRepo()
        });
        return new TokenStore(new FakeScopeFactory(sp), new FakeHttpFactory(new OkHandler()), NullLogger<TokenStore>.Instance);
    }

    private static Channel LarkApp(string appId, string secret) =>
        new() { Type = ChannelType.LarkApp, AppId = appId, Secret = secret };

    [Fact]
    public async Task Temp_token_to_static_key_removes_old_entry()
    {
        var store = CreateStore();
        await store.AddChannelAsync(LarkApp("cli_old", "sec_old"), CancellationToken.None);
        Assert.Equal("t-feishu", store.GetToken("cli_old" + "sec_old"));

        await store.UpdateChannelAsync(new Channel { Type = ChannelType.Telegram, Secret = "bot" }, LarkApp("cli_old", "sec_old"), CancellationToken.None);

        Assert.Equal("", store.GetToken("cli_old" + "sec_old"));
    }

    [Fact]
    public async Task Static_key_to_temp_token_adds_new_entry_without_old_secret()
    {
        var store = CreateStore();
        var old = new Channel { Type = ChannelType.Telegram, Secret = "bot-token" };

        await store.UpdateChannelAsync(LarkApp("cli_new", ""), old, CancellationToken.None);

        // Secret 为空 → IsFilled=false → 不添加条目，也不用旧 telegram 密钥误填充
        Assert.Equal("", store.GetToken("cli_new" + "bot-token"));
        Assert.Equal("", store.GetToken("cli_new"));

        await store.UpdateChannelAsync(LarkApp("cli_new", "sec_new"), old, CancellationToken.None);
        Assert.Equal("t-feishu", store.GetToken("cli_new" + "sec_new"));
    }

    [Fact]
    public async Task Switch_between_temp_token_types_moves_entry()
    {
        var store = CreateStore();
        await store.AddChannelAsync(LarkApp("cli_a", "sec_a"), CancellationToken.None);

        var wechatTest = new Channel { Type = ChannelType.WeChatTestAccount, AppId = "wx_app", Secret = "wx_sec" };
        await store.UpdateChannelAsync(wechatTest, LarkApp("cli_a", "sec_a"), CancellationToken.None);

        Assert.Equal("", store.GetToken("cli_a" + "sec_a"));
        Assert.Equal("t-wechat", store.GetToken("wx_app" + "wx_sec"));
    }

    [Fact]
    public async Task Same_type_update_with_empty_secret_keeps_entry()
    {
        var store = CreateStore();
        await store.AddChannelAsync(LarkApp("cli_b", "sec_b"), CancellationToken.None);

        var updated = LarkApp("cli_b", "");
        await store.UpdateChannelAsync(updated, LarkApp("cli_b", "sec_b"), CancellationToken.None);

        Assert.Equal("t-feishu", store.GetToken("cli_b" + "sec_b"));
    }
}
