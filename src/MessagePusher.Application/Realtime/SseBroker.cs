using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Json;
using MessagePusher.Domain.Entities;

namespace MessagePusher.Application.Realtime;

public sealed class SseBroker : ISseBroker
{
    private readonly Dictionary<int, Channel<Message>> _map = new();
    private readonly object _lock = new();

    public async Task SubscribeAsync(int userId, Stream responseStream, CancellationToken ct)
    {
        var ch = System.Threading.Channels.Channel.CreateBounded<Message>(new BoundedChannelOptions(10)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        lock (_lock)
            _map[userId] = ch;

        try
        {
            await foreach (var msg in ch.Reader.ReadAllAsync(ct))
            {
                var json = JsonSerializer.Serialize(msg, JsonDefaults.Options);
                var payload = $"event: message\ndata: {json}\n\n";
                var bytes = Encoding.UTF8.GetBytes(payload);
                await responseStream.WriteAsync(bytes, ct);
                await responseStream.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            lock (_lock)
            {
                if (_map.TryGetValue(userId, out var current) && ReferenceEquals(current, ch))
                    _map.Remove(userId);
            }
        }
    }

    public void Publish(int userId, Message message)
    {
        Channel<Message>? ch;
        lock (_lock)
            _map.TryGetValue(userId, out ch);
        ch?.Writer.TryWrite(message);
    }
}
