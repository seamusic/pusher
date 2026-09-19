using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Services;

public sealed class ChannelService
{
    private readonly IChannelRepository _channels;
    private readonly ITokenStore _tokens;
    private readonly IClock _clock;

    public ChannelService(IChannelRepository channels, ITokenStore tokens, IClock clock)
    {
        _channels = channels;
        _tokens = tokens;
        _clock = clock;
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
        if (input.Name.Length is 0 or > 20)
            throw new BusinessException("通道名称长度必须在1-20之间");
        if (input.Name == "email")
            throw new BusinessException("不能使用系统保留名称");
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
            clean.Status = input.Status;
        else
        {
            clean.Type = input.Type;
            clean.Name = input.Name;
            clean.Description = input.Description;
            if (!string.IsNullOrEmpty(input.Secret))
                clean.Secret = input.Secret;
            clean.AppId = input.AppId;
            clean.AccountId = input.AccountId;
            clean.Url = input.Url;
            clean.Other = input.Other;
            clean.Token = input.Token;
        }
        await _channels.UpdateAsync(clean, ct);
        await _tokens.UpdateChannelAsync(clean, old, ct);
        clean.Secret = "";
        return clean;
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
