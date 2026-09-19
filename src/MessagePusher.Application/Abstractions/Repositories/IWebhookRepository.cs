using MessagePusher.Domain.Entities;

namespace MessagePusher.Application.Abstractions.Repositories;

public interface IWebhookRepository
{
    Task<Webhook?> GetByIdAsync(int id, int userId, CancellationToken ct = default);
    Task<Webhook?> GetByLinkAsync(string link, CancellationToken ct = default);
    Task<IReadOnlyList<Webhook>> GetByUserIdAsync(int userId, int offset, int count, CancellationToken ct = default);
    Task<IReadOnlyList<Webhook>> SearchAsync(int userId, string keyword, CancellationToken ct = default);
    Task AddAsync(Webhook webhook, CancellationToken ct = default);
    Task UpdateAsync(Webhook webhook, CancellationToken ct = default);
    Task DeleteAsync(int id, int userId, CancellationToken ct = default);
}
