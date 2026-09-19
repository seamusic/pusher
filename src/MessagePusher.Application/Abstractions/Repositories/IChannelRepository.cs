using MessagePusher.Domain.Entities;

namespace MessagePusher.Application.Abstractions.Repositories;

public interface IChannelRepository
{
    Task<Channel?> GetByIdAsync(int id, int userId, bool selectAll, CancellationToken ct = default);
    Task<Channel?> GetByNameAsync(string name, int userId, CancellationToken ct = default);
    Task<IReadOnlyList<Channel>> GetByUserIdAsync(int userId, int offset, int count, CancellationToken ct = default);
    Task<IReadOnlyList<BriefChannel>> GetBriefByUserIdAsync(int userId, CancellationToken ct = default);
    Task<IReadOnlyList<Channel>> SearchAsync(int userId, string keyword, CancellationToken ct = default);
    Task<IReadOnlyList<Channel>> GetTokenStoreChannelsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Channel>> GetTokenStoreChannelsByUserIdAsync(int userId, CancellationToken ct = default);
    Task<int> CountSharedAsync(string type, string appId, string secret, CancellationToken ct = default);
    Task AddAsync(Channel channel, CancellationToken ct = default);
    Task UpdateAsync(Channel channel, CancellationToken ct = default);
    Task UpdateStatusAsync(Channel channel, int status, CancellationToken ct = default);
    Task<Channel?> DeleteAsync(int id, int userId, CancellationToken ct = default);
}
