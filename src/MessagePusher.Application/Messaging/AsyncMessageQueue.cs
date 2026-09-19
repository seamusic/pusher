using System.Threading.Channels;

namespace MessagePusher.Application.Messaging;

public sealed class AsyncMessageQueue
{
    private readonly Channel<int> _ch = Channel.CreateBounded<int>(new BoundedChannelOptions(128)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(int id, CancellationToken ct) => _ch.Writer.WriteAsync(id, ct);

    public IAsyncEnumerable<int> ReadAllAsync(CancellationToken ct) => _ch.Reader.ReadAllAsync(ct);
}
