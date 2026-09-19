using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Application.Channels;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Services;

public sealed class ChannelService
{
    private readonly IChannelRepository _channels;
    private readonly ITokenStore _tokens;
    private readonly IClock _clock;
    private readonly ChannelProviderFactory _providers;
    private readonly ChannelConfigValidatorRegistry _validators;

    public ChannelService(IChannelRepository channels, ITokenStore tokens, IClock clock,
        ChannelProviderFactory providers, ChannelConfigValidatorRegistry validators)
    {
        _channels = channels;
        _tokens = tokens;
        _clock = clock;
        _providers = providers;
        _validators = validators;
    }

    public Task<IReadOnlyList<Channel>> ListAsync(int userId, int page, CancellationToken ct) =>
        _channels.GetByUserIdAsync(userId, page * AppDefaults.ItemsPerPage, AppDefaults.ItemsPerPage, ct);

    public Task<IReadOnlyList<BriefChannel>> BriefAsync(int userId, CancellationToken ct) =>
        _channels.GetBriefByUserIdAsync(userId, ct);

    public Task<IReadOnlyList<Channel>> SearchAsync(int userId, string keyword, CancellationToken ct) =>
        _channels.SearchAsync(userId, keyword, ct);

    public async Task<Channel> GetAsync(int id, int userId, CancellationToken ct) =>
        await _channels.GetByIdAsync(id, userId, false, ct) ?? throw new BusinessException("通道不存在");

    public async Task AddAsync(int userId, Channel input, CancellationToken ct)
    {
        ValidateName(input.Name);
        var clean = new Channel
        {
            Type = input.Type,
            UserId = userId,
            Name = input.Name,
            Description = input.Description,
            Status = (int)ChannelStatus.Enabled,
            Secret = input.Secret,
            AppId = input.AppId,
            AccountId = input.AccountId,
            Url = input.Url,
            Other = input.Other,
            CreatedTime = _clock.UnixSeconds,
            Token = input.Token
        };
        ValidateTypeAndConfig(clean);
        await _channels.AddAsync(clean, ct);
        await _tokens.AddChannelAsync(clean, ct);
    }

    public async Task DeleteAsync(int id, int userId, CancellationToken ct)
    {
        var channel = await _channels.DeleteAsync(id, userId, ct) ?? throw new BusinessException("通道不存在");
        await _tokens.RemoveChannelAsync(channel, ct);
    }

    public async Task<Channel> UpdateAsync(int userId, Channel input, bool statusOnly, CancellationToken ct)
    {
        var old = await _channels.GetByIdAsync(input.Id, userId, true, ct) ?? throw new BusinessException("通道不存在");
        var clean = Clone(old);
        if (statusOnly)
        {
            // 状态更新单独处理：不要求重复提交凭证，也不重新校验完整配置。
            clean.Status = input.Status;
        }
        else
        {
            ValidateName(input.Name);
            clean.Type = input.Type;
            clean.Name = input.Name;
            clean.Description = input.Description;
            // 先合并后验证：同一 Type 下 Secret 留空保留旧值；跨 Type 编辑不得盲用旧密钥，必须提供新凭证。
            clean.Secret = !string.IsNullOrEmpty(input.Secret) || clean.Type != old.Type
                ? input.Secret
                : old.Secret;
            clean.AppId = input.AppId;
            clean.AccountId = input.AccountId;
            clean.Url = input.Url;
            clean.Other = input.Other;
            clean.Token = input.Token;
            ValidateTypeAndConfig(clean);
        }
        await _channels.UpdateAsync(clean, ct);
        await _tokens.UpdateChannelAsync(clean, old, ct);
        clean.Secret = "";
        return clean;
    }

    private static void ValidateName(string name)
    {
        if (name.Length is 0 or > 20)
            throw new BusinessException("通道名称长度必须在1-20之间");
        if (name == "email")
            throw new BusinessException("不能使用系统保留名称");
    }

    // 支持的 Type 集合来源于实际 Provider 注册；未知 Type 与未实现候选不能被保存为可用通道。
    private void ValidateTypeAndConfig(Channel channel)
    {
        try
        {
            _providers.Resolve(channel.Type);
        }
        catch (UnsupportedChannelException)
        {
            throw new BusinessException("不支持的通道类型：" + channel.Type);
        }
        _validators.Find(channel.Type)?.Validate(channel);
    }

    private static Channel Clone(Channel c) => new()
    {
        Id = c.Id,
        Type = c.Type,
        UserId = c.UserId,
        Name = c.Name,
        Description = c.Description,
        Status = c.Status,
        Secret = c.Secret,
        AppId = c.AppId,
        AccountId = c.AccountId,
        Url = c.Url,
        Other = c.Other,
        CreatedTime = c.CreatedTime,
        Token = c.Token
    };
}
