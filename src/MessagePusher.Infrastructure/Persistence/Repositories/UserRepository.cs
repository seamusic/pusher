using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MessagePusher.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;
    public UserRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<User>> GetAllAsync(int offset, int count, CancellationToken ct = default) =>
        await _db.Users.AsNoTracking()
            .OrderByDescending(x => x.Id)
            .Skip(offset).Take(count)
            .Select(x => new User
            {
                Id = x.Id,
                Username = x.Username,
                DisplayName = x.DisplayName,
                Role = x.Role,
                Status = x.Status,
                Email = x.Email,
                SendEmailToOthers = x.SendEmailToOthers,
                SaveMessageToDatabase = x.SaveMessageToDatabase
            }).ToListAsync(ct);

    public async Task<IReadOnlyList<User>> SearchAsync(string keyword, CancellationToken ct = default)
    {
        var like = keyword + "%";
        return await _db.Users.AsNoTracking()
            .Where(x => x.Id.ToString() == keyword || EF.Functions.Like(x.Username, like) || EF.Functions.Like(x.Email, like) || EF.Functions.Like(x.DisplayName, like))
            .Select(x => new User
            {
                Id = x.Id,
                Username = x.Username,
                DisplayName = x.DisplayName,
                Role = x.Role,
                Status = x.Status,
                Email = x.Email
            }).ToListAsync(ct);
    }

    public async Task<User?> GetByIdAsync(int id, bool selectAll, CancellationToken ct = default)
    {
        if (id == 0)
            return null;
        if (selectAll)
            return await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return await _db.Users.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new User
            {
                Id = x.Id,
                Username = x.Username,
                DisplayName = x.DisplayName,
                Role = x.Role,
                Status = x.Status,
                Email = x.Email,
                WeChatId = x.WeChatId,
                GitHubId = x.GitHubId,
                Channel = x.Channel,
                Token = x.Token,
                SaveMessageToDatabase = x.SaveMessageToDatabase
            }).FirstOrDefaultAsync(ct);
    }

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default) =>
        _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Username == username, ct);

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Email == email, ct);

    public Task<User?> GetByGitHubIdAsync(string githubId, CancellationToken ct = default) =>
        _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.GitHubId == githubId, ct);

    public Task<User?> GetByWeChatIdAsync(string wechatId, CancellationToken ct = default) =>
        _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.WeChatId == wechatId, ct);

    public async Task<int> GetMaxIdAsync(CancellationToken ct = default) =>
        await _db.Users.MaxAsync(x => (int?)x.Id, ct) ?? 0;

    public Task<bool> AnyAsync(CancellationToken ct = default) => _db.Users.AnyAsync(ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    public async Task UpdateAsync(User user, bool updatePassword, CancellationToken ct = default)
    {
        var tracked = await _db.Users.FirstAsync(x => x.Id == user.Id, ct);
        if (!string.IsNullOrEmpty(user.Username)) tracked.Username = user.Username;
        if (!string.IsNullOrEmpty(user.DisplayName)) tracked.DisplayName = user.DisplayName;
        if (user.Token is not null) tracked.Token = user.Token;
        if (user.Channel is not null) tracked.Channel = user.Channel;
        if (updatePassword && !string.IsNullOrEmpty(user.Password))
            tracked.Password = user.Password;
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    public async Task UpdateFieldsAsync(User user, CancellationToken ct = default)
    {
        var tracked = await _db.Users.FirstAsync(x => x.Id == user.Id, ct);
        tracked.Username = user.Username;
        tracked.DisplayName = user.DisplayName;
        tracked.Role = user.Role;
        tracked.Status = user.Status;
        tracked.Token = user.Token;
        tracked.Email = user.Email;
        tracked.GitHubId = user.GitHubId;
        tracked.WeChatId = user.WeChatId;
        tracked.Channel = user.Channel;
        tracked.SendEmailToOthers = user.SendEmailToOthers;
        tracked.SaveMessageToDatabase = user.SaveMessageToDatabase;
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await _db.Users.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task DeleteOwnedDataAsync(int userId, CancellationToken ct = default)
    {
        await _db.Channels.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await _db.Messages.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await _db.Webhooks.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
    }

    public Task<bool> EmailTakenAsync(string email, CancellationToken ct = default) =>
        _db.Users.AnyAsync(x => x.Email == email, ct);

    public Task<bool> UsernameTakenAsync(string username, CancellationToken ct = default) =>
        _db.Users.AnyAsync(x => x.Username == username, ct);

    public Task<bool> GitHubIdTakenAsync(string githubId, CancellationToken ct = default) =>
        _db.Users.AnyAsync(x => x.GitHubId == githubId, ct);

    public Task<bool> WeChatIdTakenAsync(string wechatId, CancellationToken ct = default) =>
        _db.Users.AnyAsync(x => x.WeChatId == wechatId, ct);

    public Task<bool> TokenExistsAsync(string token, CancellationToken ct = default) =>
        _db.Users.AnyAsync(x => x.Token == token, ct);

    public async Task ResetPasswordByEmailAsync(string email, string hashedPassword, CancellationToken ct = default)
    {
        await _db.Users.Where(x => x.Email == email).ExecuteUpdateAsync(s => s.SetProperty(x => x.Password, hashedPassword), ct);
    }

    public Task<int> CountAsync(CancellationToken ct = default) => _db.Users.CountAsync(ct);
}
