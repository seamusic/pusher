using MessagePusher.Application.Abstractions;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Services;

public sealed class OptionService
{
    private readonly ISystemOptionService _options;

    public OptionService(ISystemOptionService options) => _options = options;

    public IReadOnlyList<Option> GetPublic()
    {
        var list = new List<Option>();
        foreach (var (k, v) in _options.Snapshot())
        {
            if (k.Contains("Token", StringComparison.Ordinal) || k.Contains("Secret", StringComparison.Ordinal))
                continue;
            list.Add(new Option { Key = k, Value = v });
        }
        return list;
    }

    public async Task UpdateAsync(string key, string value, CancellationToken ct)
    {
        switch (key)
        {
            case "GitHubOAuthEnabled" when value == "true" && string.IsNullOrEmpty(_options.Get("GitHubClientId")):
                throw new BusinessException("无法启用 GitHub OAuth，请先填入 GitHub Client ID 以及 GitHub Client Secret！");
            case "WeChatAuthEnabled" when value == "true" && string.IsNullOrEmpty(_options.Get("WeChatServerAddress")):
                throw new BusinessException("无法启用微信登录，请先填入微信登录相关配置信息！");
            case "TurnstileCheckEnabled" when value == "true" && string.IsNullOrEmpty(_options.Get("TurnstileSiteKey")):
                throw new BusinessException("无法启用 Turnstile 校验，请先填入 Turnstile 校验相关配置信息！");
        }
        await _options.UpdateAsync(key, value, ct);
    }
}
