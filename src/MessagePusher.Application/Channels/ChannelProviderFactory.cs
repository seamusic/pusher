using MessagePusher.Application.Abstractions;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Channels;

public sealed class ChannelProviderFactory
{
    private readonly IReadOnlyDictionary<string, IChannelProvider> _map;

    public ChannelProviderFactory(IEnumerable<IChannelProvider> providers)
    {
        _map = providers.ToDictionary(p => p.Type, StringComparer.Ordinal);
    }

    public IChannelProvider Resolve(string type) =>
        _map.TryGetValue(type, out var p) ? p : throw new UnsupportedChannelException(type);
}
