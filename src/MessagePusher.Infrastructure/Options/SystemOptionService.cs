using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace MessagePusher.Infrastructure.Options;

public sealed class SystemOptionService : ISystemOptionService
{
    private readonly Dictionary<string, string> _map = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly IServiceScopeFactory _scopeFactory;

    public SystemOptionService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public string Get(string key, string fallback = "")
    {
        _lock.EnterReadLock();
        try
        {
            return _map.TryGetValue(key, out var v) ? v : fallback;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool GetBool(string key, bool fallback = false)
    {
        var v = Get(key, fallback ? "true" : "false");
        return v == "true";
    }

    public int GetInt(string key, int fallback = 0)
    {
        var v = Get(key, fallback.ToString());
        return int.TryParse(v, out var n) ? n : fallback;
    }

    public IReadOnlyDictionary<string, string> Snapshot()
    {
        _lock.EnterReadLock();
        try
        {
            return new Dictionary<string, string>(_map);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public async Task InitAsync(CancellationToken ct = default)
    {
        _lock.EnterWriteLock();
        try
        {
            _map.Clear();
            _map["FileUploadPermission"] = Roles.Guest.ToString();
            _map["FileDownloadPermission"] = Roles.Guest.ToString();
            _map["ImageUploadPermission"] = Roles.Guest.ToString();
            _map["ImageDownloadPermission"] = Roles.Guest.ToString();
            _map["PasswordLoginEnabled"] = "true";
            _map["PasswordRegisterEnabled"] = "true";
            _map["EmailVerificationEnabled"] = "false";
            _map["GitHubOAuthEnabled"] = "false";
            _map["WeChatAuthEnabled"] = "false";
            _map["TurnstileCheckEnabled"] = "false";
            _map["RegisterEnabled"] = "true";
            _map["MessagePersistenceEnabled"] = "true";
            _map["MessageRenderEnabled"] = "true";
            _map["SMTPServer"] = "";
            _map["SMTPAccount"] = "";
            _map["SMTPPort"] = AppDefaults.SmtpPort.ToString();
            _map["SMTPToken"] = "";
            _map["Notice"] = "";
            _map["About"] = "";
            _map["Footer"] = "";
            _map["HomePageLink"] = "";
            _map["ServerAddress"] = AppDefaults.ServerAddress;
            _map["SystemName"] = AppDefaults.SystemName;
            _map["GitHubClientId"] = "";
            _map["GitHubClientSecret"] = "";
            _map["WeChatServerAddress"] = "";
            _map["WeChatServerToken"] = "";
            _map["WeChatAccountQRCodeImageURL"] = "";
            _map["TurnstileSiteKey"] = "";
            _map["TurnstileSecretKey"] = "";
            _map["SmtpSkipCertificateValidation"] = "false";
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOptionRepository>();
        var options = await repo.GetAllAsync(ct);
        foreach (var o in options)
            SetMemory(o.Key, o.Value);
    }

    public async Task UpdateAsync(string key, string value, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOptionRepository>();
        await repo.UpsertAsync(key, value, ct);
        SetMemory(key, value);
    }

    private void SetMemory(string key, string value)
    {
        _lock.EnterWriteLock();
        try
        {
            _map[key] = value;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}
