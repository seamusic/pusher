using MessagePusher.Application.Abstractions;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Infrastructure.Crypto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MessagePusher.Infrastructure.Persistence;

public sealed class DatabaseInitializer
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly AppCounters _counters;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(AppDbContext db, IPasswordHasher hasher, AppCounters counters, ILogger<DatabaseInitializer> logger)
    {
        _db = db;
        _hasher = hasher;
        _counters = counters;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _db.Database.MigrateAsync(ct);
        if (!await _db.Users.AnyAsync(ct))
        {
            _logger.LogInformation("no user exists, create a root user for you: username is root, password is 123456");
            _db.Users.Add(new User
            {
                Username = AppDefaults.RootUsername,
                Password = _hasher.Hash(AppDefaults.RootPassword),
                Role = Roles.Root,
                Status = (int)UserStatus.Enabled,
                DisplayName = AppDefaults.RootDisplayName
            });
            await _db.SaveChangesAsync(ct);
        }
        _counters.SetUserCount(await _db.Users.CountAsync(ct));
        _counters.SetMessageCount(await _db.Messages.CountAsync(ct));
    }
}
