using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MessagePusher.Infrastructure.Persistence.Repositories;

public sealed class OptionRepository : IOptionRepository
{
    private readonly AppDbContext _db;
    public OptionRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Option>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Options.AsNoTracking().ToListAsync(ct);

    public async Task UpsertAsync(string key, string value, CancellationToken ct = default)
    {
        var option = await _db.Options.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (option is null)
            _db.Options.Add(new Option { Key = key, Value = value });
        else
            option.Value = value;
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }
}
