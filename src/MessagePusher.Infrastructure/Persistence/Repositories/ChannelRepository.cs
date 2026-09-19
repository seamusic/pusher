using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MessagePusher.Infrastructure.Persistence.Repositories;

public sealed class ChannelRepository : IChannelRepository
{
    private readonly AppDbContext _db;
    public ChannelRepository(AppDbContext db) => _db = db;

    public async Task<Channel?> GetByIdAsync(int id, int userId, bool selectAll, CancellationToken ct = default)
    {
        if (id == 0 || userId == 0)
            return null;
        var q = _db.Channels.AsNoTracking().Where(x => x.Id == id && x.UserId == userId);
        if (selectAll)
            return await q.FirstOrDefaultAsync(ct);
        return await q.Select(x => new Channel
        {
            Id = x.Id,
            Type = x.Type,
            UserId = x.UserId,
            Name = x.Name,
            Description = x.Description,
            Status = x.Status,
            AppId = x.AppId,
            AccountId = x.AccountId,
            Url = x.Url,
            Other = x.Other,
            CreatedTime = x.CreatedTime,
            Token = x.Token
        }).FirstOrDefaultAsync(ct);
    }

    public Task<Channel?> GetByNameAsync(string name, int userId, CancellationToken ct = default) =>
        _db.Channels.AsNoTracking().FirstOrDefaultAsync(x => x.Name == name && x.UserId == userId, ct);

    public async Task<IReadOnlyList<Channel>> GetByUserIdAsync(int userId, int offset, int count, CancellationToken ct = default) =>
        await _db.Channels.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.Id)
            .Skip(offset).Take(count)
            .Select(OmitSecret)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BriefChannel>> GetBriefByUserIdAsync(int userId, CancellationToken ct = default) =>
        await _db.Channels.AsNoTracking()
            .Where(x => x.UserId == userId && x.Status == (int)ChannelStatus.Enabled)
            .Select(x => new BriefChannel { Id = x.Id, Name = x.Name, Description = x.Description })
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Channel>> SearchAsync(int userId, string keyword, CancellationToken ct = default)
    {
        var like = keyword + "%";
        return await _db.Channels.AsNoTracking()
            .Where(x => x.UserId == userId && (x.Id.ToString() == keyword || EF.Functions.Like(x.Name, like)))
            .Select(OmitSecret)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Channel>> GetTokenStoreChannelsAsync(CancellationToken ct = default) =>
        await _db.Channels.AsNoTracking()
            .Where(x => x.Type == ChannelType.WeChatCorpAccount || x.Type == ChannelType.WeChatTestAccount || x.Type == ChannelType.LarkApp)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Channel>> GetTokenStoreChannelsByUserIdAsync(int userId, CancellationToken ct = default) =>
        await _db.Channels.AsNoTracking()
            .Where(x => x.UserId == userId && (x.Type == ChannelType.WeChatCorpAccount || x.Type == ChannelType.WeChatTestAccount || x.Type == ChannelType.LarkApp))
            .ToListAsync(ct);

    public Task<int> CountSharedAsync(string type, string appId, string secret, CancellationToken ct = default) =>
        _db.Channels.CountAsync(x => x.Type == type && x.AppId == appId && x.Secret == secret, ct);

    public async Task AddAsync(Channel channel, CancellationToken ct = default)
    {
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    public async Task UpdateAsync(Channel channel, CancellationToken ct = default)
    {
        var tracked = await _db.Channels.FirstAsync(x => x.Id == channel.Id, ct);
        tracked.Type = channel.Type;
        tracked.Name = channel.Name;
        tracked.Description = channel.Description;
        tracked.Secret = channel.Secret;
        tracked.AppId = channel.AppId;
        tracked.AccountId = channel.AccountId;
        tracked.Url = channel.Url;
        tracked.Other = channel.Other;
        tracked.Status = channel.Status;
        tracked.Token = channel.Token;
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    public async Task UpdateStatusAsync(Channel channel, int status, CancellationToken ct = default)
    {
        await _db.Channels.Where(x => x.Id == channel.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status), ct);
    }

    public async Task<Channel?> DeleteAsync(int id, int userId, CancellationToken ct = default)
    {
        var c = await _db.Channels.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (c is null)
            return null;
        _db.Channels.Remove(c);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return c;
    }

    private static readonly System.Linq.Expressions.Expression<Func<Channel, Channel>> OmitSecret = x => new Channel
    {
        Id = x.Id,
        Type = x.Type,
        UserId = x.UserId,
        Name = x.Name,
        Description = x.Description,
        Status = x.Status,
        AppId = x.AppId,
        AccountId = x.AccountId,
        Url = x.Url,
        Other = x.Other,
        CreatedTime = x.CreatedTime,
        Token = x.Token
    };
}
