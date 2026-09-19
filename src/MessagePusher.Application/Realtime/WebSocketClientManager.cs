using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Json;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace MessagePusher.Application.Realtime;

public sealed class WebSocketClientManager : IWebSocketClientManager
{
    private static readonly TimeSpan WriteWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PongWait = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PingPeriod = TimeSpan.FromSeconds(54);
    private const int MaxMessageSize = 512;

    private readonly Dictionary<string, Client> _clients = new();
    private readonly object _lock = new();
    private readonly ISystemOptionService _options;
    private readonly ILogger<WebSocketClientManager> _logger;

    public WebSocketClientManager(ISystemOptionService options, ILogger<WebSocketClientManager> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task RegisterAsync(string channelName, int userId, WebSocket socket, CancellationToken ct)
    {
        var key = $"{channelName}:{userId}";
        Client? old;
        lock (_lock)
            _clients.TryGetValue(key, out old);

        if (old is not null)
        {
            var bye = new Message
            {
                Title = _options.Get("SystemName", Domain.AppDefaults.SystemName),
                Description = "其他客户端已连接服务器，本客户端已被挤下线！"
            };
            await old.SendAsync(bye);
            await old.CloseAsync();
        }

        var client = new Client(key, socket, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _logger);
        lock (_lock)
            _clients[key] = client;

        var hello = new Message
        {
            Title = _options.Get("SystemName", Domain.AppDefaults.SystemName),
            Description = "客户端连接成功！"
        };
        _ = client.RunAsync(RemoveIfSame, hello, ct);
    }

    public async Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
    {
        var key = $"{channel.Name}:{user.Id}";
        Client? client;
        lock (_lock)
            _clients.TryGetValue(key, out client);
        if (client is null)
            throw new BusinessException("客户端未连接");
        await client.SendAsync(message);
    }

    private void RemoveIfSame(Client client)
    {
        lock (_lock)
        {
            if (_clients.TryGetValue(client.Key, out var current) && current.Timestamp == client.Timestamp)
                _clients.Remove(client.Key);
        }
    }

    private sealed class Client
    {
        public string Key { get; }
        public long Timestamp { get; }
        private readonly WebSocket _socket;
        private readonly System.Threading.Channels.Channel<Message> _messages = System.Threading.Channels.Channel.CreateUnbounded<Message>();
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new();

        public Client(string key, WebSocket socket, long timestamp, ILogger logger)
        {
            Key = key;
            Timestamp = timestamp;
            _socket = socket;
            _logger = logger;
        }

        public ValueTask SendAsync(Message message) => _messages.Writer.WriteAsync(message);

        public async Task CloseAsync()
        {
            _cts.Cancel();
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "error close WebSocket client");
            }
        }

        public async Task RunAsync(Action<Client> onClosed, Message hello, CancellationToken hostCt)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(hostCt, _cts.Token);
            var ct = linked.Token;
            try
            {
                await SendRawAsync(hello, ct);
                var write = WriteLoopAsync(ct);
                var read = ReadLoopAsync(ct);
                await Task.WhenAny(write, read);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "error WebSocket client");
            }
            finally
            {
                onClosed(this);
                try { _socket.Dispose(); } catch { /* ignore */ }
            }
        }

        private async Task WriteLoopAsync(CancellationToken ct)
        {
            using var pingTimer = new PeriodicTimer(PingPeriod);
            var pingTask = pingTimer.WaitForNextTickAsync(ct).AsTask();
            var msgTask = _messages.Reader.ReadAsync(ct).AsTask();
            while (!ct.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var completed = await Task.WhenAny(pingTask, msgTask);
                if (completed == pingTask)
                {
                    if (await pingTask)
                        await SendFrameAsync(WebSocketMessageType.Binary, Encoding.UTF8.GetBytes(""), true, ct, ping: true);
                    pingTask = pingTimer.WaitForNextTickAsync(ct).AsTask();
                }
                else
                {
                    var msg = await msgTask;
                    await SendRawAsync(msg, ct);
                    msgTask = _messages.Reader.ReadAsync(ct).AsTask();
                }
            }
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[MaxMessageSize];
            while (!ct.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var result = await _socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }

        private async Task SendRawAsync(Message message, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(message, JsonDefaults.Options);
            var bytes = Encoding.UTF8.GetBytes(json);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(WriteWait);
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);
        }

        private async Task SendFrameAsync(WebSocketMessageType type, byte[] bytes, bool end, CancellationToken ct, bool ping = false)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(WriteWait);
            if (ping)
                await _socket.SendAsync(Array.Empty<byte>(), WebSocketMessageType.Binary, WebSocketMessageFlags.EndOfMessage | WebSocketMessageFlags.DisableCompression, cts.Token);
            else
                await _socket.SendAsync(bytes, type, end, cts.Token);
        }
    }
}
