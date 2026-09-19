using MessagePusher.Domain.Entities;

namespace MessagePusher.Application.Abstractions.Repositories;

public interface IMessageRepository
{
    Task<Message?> GetByIdsAsync(int id, int userId, CancellationToken ct = default);
    Task<Message?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Message?> GetByLinkAsync(string link, CancellationToken ct = default);
    Task<int> GetStatusByLinkAsync(string link, CancellationToken ct = default);
    Task<IReadOnlyList<Message>> GetByUserIdAsync(int userId, int offset, int count, CancellationToken ct = default);
    Task<IReadOnlyList<Message>> SearchAsync(int userId, string keyword, CancellationToken ct = default);
    Task<IReadOnlyList<int>> GetAsyncPendingIdsAsync(CancellationToken ct = default);
    Task AddAsync(Message message, CancellationToken ct = default);
    Task UpdateStatusAsync(Message message, int status, CancellationToken ct = default);
    Task DeleteAsync(int id, int userId, CancellationToken ct = default);
    Task DeleteAllAsync(CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
}
