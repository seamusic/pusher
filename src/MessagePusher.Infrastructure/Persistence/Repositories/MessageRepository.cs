using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MessagePusher.Infrastructure.Persistence.Repositories;

public sealed class MessageRepository : IMessageRepository
{
    private readonly AppDbContext _db;
    public MessageRepository(AppDbContext db) => _db = db;

    public Task<Message?> GetByIdsAsync(int id, int userId, CancellationToken ct = default) =>
        _db.Messages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

    public Task<Message?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Messages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Message?> GetByLinkAsync(string link, CancellationToken ct = default) =>
        _db.Messages.AsNoTracking().FirstOrDefaultAsync(x => x.Link == link, ct);

    public async Task<int> GetStatusByLinkAsync(string link, CancellationToken ct = default)
    {
        var status = await _db.Messages.AsNoTracking().Where(x => x.Link == link).Select(x => (int?)x.Status).FirstOrDefaultAsync(ct);
        if (status is null)
            throw new InvalidOperationException("link 为空！");
        return status.Value;
    }

    public async Task<IReadOnlyList<Message>> GetByUserIdAsync(int userId, int offset, int count, CancellationToken ct = default) =>
        await _db.Messages.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.Id)
            .Skip(offset).Take(count)
            .Select(x => new Message { Id = x.Id, Title = x.Title, Channel = x.Channel, Timestamp = x.Timestamp, Status = x.Status })
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Message>> SearchAsync(int userId, string keyword, CancellationToken ct = default)
    {
        var like = keyword + "%";
        return await _db.Messages.AsNoTracking()
            .Where(x => x.UserId == userId && (x.Id.ToString() == keyword || EF.Functions.Like(x.Title, like) || EF.Functions.Like(x.Description, like) || EF.Functions.Like(x.Content, like)))
            .OrderByDescending(x => x.Id)
            .Select(x => new Message { Id = x.Id, Title = x.Title, Channel = x.Channel, Timestamp = x.Timestamp, Status = x.Status })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<int>> GetAsyncPendingIdsAsync(CancellationToken ct = default) =>
        await _db.Messages.Where(x => x.Status == (int)MessageSendStatus.AsyncPending).Select(x => x.Id).ToListAsync(ct);

    public async Task AddAsync(Message message, CancellationToken ct = default)
    {
        _db.Messages.Add(message);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    public async Task UpdateStatusAsync(Message message, int status, CancellationToken ct = default)
    {
        await _db.Messages.Where(x => x.Id == message.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status), ct);
        message.Status = status;
    }

    public async Task DeleteAsync(int id, int userId, CancellationToken ct = default)
    {
        await _db.Messages.Where(x => x.Id == id && x.UserId == userId).ExecuteDeleteAsync(ct);
    }

    public async Task DeleteAllAsync(CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM messages", ct);
    }

    public Task<int> CountAsync(CancellationToken ct = default) => _db.Messages.CountAsync(ct);
}
