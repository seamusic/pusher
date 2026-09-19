using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MessagePusher.Infrastructure.Persistence.Repositories;

public sealed class WebhookRepository : IWebhookRepository
{
    private readonly AppDbContext _db;
    public WebhookRepository(AppDbContext db) => _db = db;

    public Task<Webhook?> GetByIdAsync(int id, int userId, CancellationToken ct = default) =>
        _db.Webhooks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

    public Task<Webhook?> GetByLinkAsync(string link, CancellationToken ct = default) =>
        _db.Webhooks.AsNoTracking().FirstOrDefaultAsync(x => x.Link == link, ct);

    public async Task<IReadOnlyList<Webhook>> GetByUserIdAsync(int userId, int offset, int count, CancellationToken ct = default) =>
        await _db.Webhooks.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.Id)
            .Skip(offset).Take(count)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Webhook>> SearchAsync(int userId, string keyword, CancellationToken ct = default)
    {
        var like = keyword + "%";
        return await _db.Webhooks.AsNoTracking()
            .Where(x => x.UserId == userId && (x.Id.ToString() == keyword || x.Link == keyword || EF.Functions.Like(x.Name, like)))
            .ToListAsync(ct);
    }

    public async Task AddAsync(Webhook webhook, CancellationToken ct = default)
    {
        _db.Webhooks.Add(webhook);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    public async Task UpdateAsync(Webhook webhook, CancellationToken ct = default)
    {
        var tracked = await _db.Webhooks.FirstAsync(x => x.Id == webhook.Id, ct);
        tracked.Status = webhook.Status;
        tracked.Name = webhook.Name;
        tracked.ExtractRule = webhook.ExtractRule;
        tracked.ConstructRule = webhook.ConstructRule;
        tracked.Channel = webhook.Channel;
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    public async Task DeleteAsync(int id, int userId, CancellationToken ct = default)
    {
        await _db.Webhooks.Where(x => x.Id == id && x.UserId == userId).ExecuteDeleteAsync(ct);
    }
}
