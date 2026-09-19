using MessagePusher.Domain.Entities;

namespace MessagePusher.Application.Abstractions.Repositories;

public interface IUserRepository
{
    Task<IReadOnlyList<User>> GetAllAsync(int offset, int count, CancellationToken ct = default);
    Task<IReadOnlyList<User>> SearchAsync(string keyword, CancellationToken ct = default);
    Task<User?> GetByIdAsync(int id, bool selectAll, CancellationToken ct = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> GetByGitHubIdAsync(string githubId, CancellationToken ct = default);
    Task<User?> GetByWeChatIdAsync(string wechatId, CancellationToken ct = default);
    Task<int> GetMaxIdAsync(CancellationToken ct = default);
    Task<bool> AnyAsync(CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, bool updatePassword, CancellationToken ct = default);
    Task UpdateFieldsAsync(User user, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task DeleteOwnedDataAsync(int userId, CancellationToken ct = default);
    Task<bool> EmailTakenAsync(string email, CancellationToken ct = default);
    Task<bool> UsernameTakenAsync(string username, CancellationToken ct = default);
    Task<bool> GitHubIdTakenAsync(string githubId, CancellationToken ct = default);
    Task<bool> WeChatIdTakenAsync(string wechatId, CancellationToken ct = default);
    Task<bool> TokenExistsAsync(string token, CancellationToken ct = default);
    Task ResetPasswordByEmailAsync(string email, string hashedPassword, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
}
