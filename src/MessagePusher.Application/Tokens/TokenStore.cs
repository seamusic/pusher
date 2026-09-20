using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MessagePusher.Application.Tokens;

public interface ITokenStoreItem
{
    string Key { get; }
    string Token { get; }
    bool IsFilled { get; }
    string Type { get; }
    string AppId { get; }
    string Secret { get; }
    Task RefreshAsync(CancellationToken ct);
}

public sealed class TokenStore : BackgroundService, ITokenStore
{
    private readonly Dictionary<string, ITokenStoreItem> _map = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<TokenStore> _logger;
    private const int ExpirationSeconds = AppDefaults.TokenStoreExpirationSeconds;

    public TokenStore(IServiceScopeFactory scopeFactory, IHttpClientFactory http, ILogger<TokenStore> logger)
    {
        _scopeFactory = scopeFactory;
        _http = http;
        _logger = logger;
    }

    public string GetToken(string key)
    {
        _lock.EnterReadLock();
        try
        {
            if (_map.TryGetValue(key, out var item))
                return item.Token;
            _logger.LogError("token for {Key} is blank!", key);
            return "";
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public async Task AddChannelAsync(Channel channel, CancellationToken ct = default)
    {
        if (!IsTokenStoreType(channel.Type))
            return;
        var item = Channel2Item(channel);
        if (item is not null)
            await AddItemAsync(item, ct);
    }

    public async Task RemoveChannelAsync(Channel channel, CancellationToken ct = default)
    {
        if (!IsTokenStoreType(channel.Type))
            return;
        var item = Channel2Item(channel);
        if (item is not null && !await HasRemainingReferencesAsync(item, ct))
            RemoveItem(item);
    }

    public async Task UpdateChannelAsync(Channel newChannel, Channel oldChannel, CancellationToken ct = default)
    {
        // 跨 Type 编辑时旧条目移除与新条目添加相互独立：
        // 旧 Type 是临时 Token 类型才移除；新 Type 是临时 Token 类型才添加。
        if (IsTokenStoreType(oldChannel.Type))
        {
            var oldItem = Channel2Item(oldChannel);
            if (oldItem is not null && !await HasRemainingReferencesAsync(oldItem, ct))
                RemoveItem(oldItem);
        }

        if (IsTokenStoreType(newChannel.Type))
        {
            // 仅同 Type 编辑沿用"留空保留旧值"的合并；跨 Type 切换一律使用新凭证，防止旧密钥误填充。
            var sameType = newChannel.Type == oldChannel.Type;
            var merged = Channel2Item(new Channel
            {
                Type = newChannel.Type,
                AppId = sameType && (string.IsNullOrEmpty(newChannel.AppId) || newChannel.AppId == oldChannel.AppId)
                    ? oldChannel.AppId : newChannel.AppId,
                Secret = sameType && (string.IsNullOrEmpty(newChannel.Secret) || newChannel.Secret == oldChannel.Secret)
                    ? oldChannel.Secret : newChannel.Secret
            });
            if (merged is not null)
                await AddItemAsync(merged, ct);
        }
    }

    public async Task AddUserAsync(User user, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
        var channels = await repo.GetTokenStoreChannelsByUserIdAsync(user.Id, ct);
        foreach (var ch in channels)
            await AddChannelAsync(ch, ct);
    }

    public async Task RemoveUserAsync(User user, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
        var channels = await repo.GetTokenStoreChannelsByUserIdAsync(user.Id, ct);
        // 此路径在删库前调用，排除该用户全部引用，保留其他用户共用的 Token。
        foreach (var group in channels.GroupBy(ch => (ch.Type, ch.AppId, ch.Secret)))
        {
            var item = Channel2Item(group.First());
            if (item is not null && !await HasRemainingReferencesAsync(item, ct, group.Count()))
                RemoveItem(item);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
            var channels = await repo.GetTokenStoreChannelsAsync(stoppingToken);
            foreach (var ch in channels)
            {
                var item = Channel2Item(ch);
                if (item is not null)
                    await AddItemAsync(item, stoppingToken);
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            List<ITokenStoreItem> snapshot;
            _lock.EnterReadLock();
            try
            {
                snapshot = _map.Values.ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }

            foreach (var item in snapshot)
            {
                try
                {
                    await item.RefreshAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "failed to refresh access token");
                }
            }

            var sleep = Math.Max(ExpirationSeconds, 60);
            await Task.Delay(TimeSpan.FromSeconds(sleep), stoppingToken);
        }
    }

    private async Task AddItemAsync(ITokenStoreItem item, CancellationToken ct)
    {
        if (!item.IsFilled)
            return;
        await item.RefreshAsync(ct);
        _lock.EnterWriteLock();
        try
        {
            _map[item.Key] = item;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private void RemoveItem(ITokenStoreItem item)
    {
        _lock.EnterWriteLock();
        try
        {
            _map.Remove(item.Key);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    // 通道编辑/删除已落库，剩余一个引用也必须保留；用户清理可排除待移除的引用。
    private async Task<bool> HasRemainingReferencesAsync(ITokenStoreItem item, CancellationToken ct, int excludedReferences = 0)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
        return await repo.CountSharedAsync(item.Type, item.AppId, item.Secret, ct) > excludedReferences;
    }

    private ITokenStoreItem? Channel2Item(Channel channel) => channel.Type switch
    {
        ChannelType.WeChatTestAccount => new WeChatTestTokenItem(_http, _logger)
        {
            AppId = channel.AppId,
            Secret = channel.Secret
        },
        ChannelType.WeChatCorpAccount => CreateCorpItem(channel),
        ChannelType.LarkApp => new LarkAppTokenItem(_http, _logger)
        {
            AppId = channel.AppId,
            Secret = channel.Secret
        },
        _ => null
    };

    private ITokenStoreItem? CreateCorpItem(Channel channel)
    {
        var parts = channel.AppId.Split('|');
        if (parts.Length != 2)
        {
            _logger.LogError("无效的微信企业号配置");
            return null;
        }
        return new WeChatCorpTokenItem(_http, _logger)
        {
            AppId = channel.AppId,
            Secret = channel.Secret,
            CorpId = parts[0],
            AgentId = parts[1]
        };
    }

    private static bool IsTokenStoreType(string type) =>
        type is ChannelType.WeChatTestAccount or ChannelType.WeChatCorpAccount or ChannelType.LarkApp;
}

public sealed class WeChatTestTokenItem : ITokenStoreItem
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger _logger;
    public string AppId { get; set; } = "";
    public string Secret { get; set; } = "";
    public string Token { get; private set; } = "";
    public string Key => AppId + Secret;
    public bool IsFilled => AppId != "" && Secret != "";
    public string Type => ChannelType.WeChatTestAccount;

    public WeChatTestTokenItem(IHttpClientFactory http, ILogger logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient("token-store");
            var url = $"https://api.weixin.qq.com/cgi-bin/token?grant_type=client_credential&appid={AppId}&secret={Secret}";
            var res = await client.GetFromJsonAsync<WeChatTokenResponse>(url, ct);
            if (res is null)
                return;
            if (res.ErrCode != 0)
            {
                _logger.LogError("{Msg}", res.ErrMsg);
                return;
            }
            Token = res.AccessToken ?? "";
            _logger.LogInformation("access token refreshed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "failed to refresh access token");
        }
    }

    private sealed class WeChatTokenResponse
    {
        [JsonPropertyName("errcode")] public int ErrCode { get; set; }
        [JsonPropertyName("errmsg")] public string? ErrMsg { get; set; }
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    }
}

public sealed class WeChatCorpTokenItem : ITokenStoreItem
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger _logger;
    public string AppId { get; set; } = "";
    public string Secret { get; set; } = "";
    public string CorpId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string Token { get; private set; } = "";
    public string Key => CorpId + AgentId + Secret;
    public bool IsFilled => CorpId != "" && Secret != "" && AgentId != "";
    public string Type => ChannelType.WeChatCorpAccount;

    public WeChatCorpTokenItem(IHttpClientFactory http, ILogger logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient("token-store");
            var url = $"https://qyapi.weixin.qq.com/cgi-bin/gettoken?corpid={CorpId}&corpsecret={Secret}";
            var res = await client.GetFromJsonAsync<WeChatTokenResponse>(url, ct);
            if (res is null)
                return;
            if (res.ErrCode != 0)
            {
                _logger.LogError("{Msg}", res.ErrMsg);
                return;
            }
            Token = res.AccessToken ?? "";
            _logger.LogInformation("access token refreshed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "failed to refresh access token");
        }
    }

    private sealed class WeChatTokenResponse
    {
        [JsonPropertyName("errcode")] public int ErrCode { get; set; }
        [JsonPropertyName("errmsg")] public string? ErrMsg { get; set; }
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    }
}

public sealed class LarkAppTokenItem : ITokenStoreItem
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger _logger;
    public string AppId { get; set; } = "";
    public string Secret { get; set; } = "";
    public string Token { get; private set; } = "";
    public string Key => AppId + Secret;
    public bool IsFilled => AppId != "" && Secret != "";
    public string Type => ChannelType.LarkApp;

    public LarkAppTokenItem(IHttpClientFactory http, ILogger logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient("token-store");
            var res = await client.PostAsJsonAsync("https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal",
                new { app_id = AppId, app_secret = Secret }, ct);
            var body = await res.Content.ReadFromJsonAsync<LarkTokenResponse>(ct);
            if (body is null)
                return;
            if (body.Code != 0)
            {
                _logger.LogError("{Msg}", body.Msg);
                return;
            }
            Token = body.TenantAccessToken ?? "";
            _logger.LogInformation("access token refreshed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "failed to refresh access token");
        }
    }

    private sealed class LarkTokenResponse
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("msg")] public string? Msg { get; set; }
        [JsonPropertyName("tenant_access_token")] public string? TenantAccessToken { get; set; }
    }
}
