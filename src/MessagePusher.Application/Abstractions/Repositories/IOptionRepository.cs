using MessagePusher.Domain.Entities;

namespace MessagePusher.Application.Abstractions.Repositories;

public interface IOptionRepository
{
    Task<IReadOnlyList<Option>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(string key, string value, CancellationToken ct = default);
}
