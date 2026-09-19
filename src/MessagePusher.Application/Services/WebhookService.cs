using System.Text.Json;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Application.Json;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Services;

public sealed class WebhookService
{
    private readonly IWebhookRepository _webhooks;
    private readonly IUserRepository _users;
    private readonly IGuidGenerator _guid;
    private readonly IClock _clock;
    private readonly IGjson _gjson;
    private readonly ISystemOptionService _options;
    private readonly PushService _push;

    public WebhookService(
        IWebhookRepository webhooks,
        IUserRepository users,
        IGuidGenerator guid,
        IClock clock,
        IGjson gjson,
        ISystemOptionService options,
        PushService push)
    {
        _webhooks = webhooks;
        _users = users;
        _guid = guid;
        _clock = clock;
        _gjson = gjson;
        _options = options;
        _push = push;
    }

    public Task<IReadOnlyList<Webhook>> ListAsync(int userId, int page, CancellationToken ct) =>
        _webhooks.GetByUserIdAsync(userId, page * AppDefaults.ItemsPerPage, AppDefaults.ItemsPerPage, ct);

    public Task<IReadOnlyList<Webhook>> SearchAsync(int userId, string keyword, CancellationToken ct)
    {
        var prefix = _options.Get("ServerAddress", AppDefaults.ServerAddress) + "/webhook/";
        if (keyword.StartsWith(prefix, StringComparison.Ordinal))
            keyword = keyword[prefix.Length..];
        return _webhooks.SearchAsync(userId, keyword, ct);
    }

    public async Task<Webhook> GetAsync(int id, int userId, CancellationToken ct) =>
        await _webhooks.GetByIdAsync(id, userId, ct) ?? throw new BusinessException("Webhook 不存在");

    public async Task AddAsync(int userId, Webhook input, CancellationToken ct)
    {
        if (input.Name.Length is 0 or > 20)
            throw new BusinessException("Webhook 名称长度必须在1-20之间");
        var clean = new Webhook
        {
            UserId = userId,
            Name = input.Name,
            Status = (int)WebhookStatus.Enabled,
            Link = _guid.NewN(),
            CreatedTime = _clock.UnixSeconds,
            Channel = input.Channel,
            ExtractRule = input.ExtractRule,
            ConstructRule = input.ConstructRule
        };
        await _webhooks.AddAsync(clean, ct);
    }

    public Task DeleteAsync(int id, int userId, CancellationToken ct) => _webhooks.DeleteAsync(id, userId, ct);

    public async Task<Webhook> UpdateAsync(int userId, Webhook input, bool statusOnly, CancellationToken ct)
    {
        var old = await _webhooks.GetByIdAsync(input.Id, userId, ct) ?? throw new BusinessException("Webhook 不存在");
        if (statusOnly)
            old.Status = input.Status;
        else
        {
            old.Name = input.Name;
            old.ExtractRule = input.ExtractRule;
            old.ConstructRule = input.ConstructRule;
            old.Channel = input.Channel;
        }
        await _webhooks.UpdateAsync(old, ct);
        return old;
    }

    public async Task<string> TriggerAsync(string link, string body, CancellationToken ct)
    {
        var webhook = await _webhooks.GetByLinkAsync(link, ct) ?? throw new WebhookHttpException(404, "Webhook 不存在");
        if (webhook.Status != (int)WebhookStatus.Enabled)
            throw new WebhookHttpException(403, "Webhook 未启用");
        var user = await _users.GetByIdAsync(webhook.UserId, false, ct) ?? throw new WebhookHttpException(404, "用户不存在");
        if (user.Status != (int)UserStatus.Enabled)
            throw new WebhookHttpException(403, "用户已被封禁");

        Dictionary<string, string>? extractRule;
        try
        {
            extractRule = JsonSerializer.Deserialize<Dictionary<string, string>>(webhook.ExtractRule, JsonDefaults.Options);
        }
        catch
        {
            throw new WebhookHttpException(400, "Webhook 提取规则解析失败");
        }
        if (extractRule is null)
            throw new WebhookHttpException(400, "Webhook 提取规则解析失败");

        var construct = webhook.ConstructRule;
        foreach (var (key, path) in extractRule)
        {
            var value = _gjson.GetString(body, path);
            construct = QuoteReplace(construct, "$" + key, value);
        }

        WebhookConstructRule? rule;
        try
        {
            rule = JsonSerializer.Deserialize<WebhookConstructRule>(construct, JsonDefaults.Options);
        }
        catch
        {
            throw new WebhookHttpException(400, "Webhook 构建规则解析失败");
        }
        if (rule is null)
            throw new WebhookHttpException(400, "Webhook 构建规则解析失败");

        var message = new Message
        {
            Channel = webhook.Channel,
            Title = rule.Title,
            Description = rule.Description,
            Content = rule.Content,
            Url = rule.Url
        };
        await _push.ProcessAsync(message, user, false, ct);
        return message.Link;
    }

    private static string QuoteReplace(string s, string old, string value)
    {
        var quoted = JsonSerializer.Serialize(value);
        if (quoted.Length >= 2)
            quoted = quoted[1..^1];
        return s.Replace(old, quoted);
    }
}

public sealed class WebhookHttpException : Exception
{
    public int StatusCode { get; }
    public WebhookHttpException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}
