using MessagePusher.Domain.Entities;

namespace MessagePusher.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface IGuidGenerator
{
    string NewN();
}

public interface IMarkdownRenderer
{
    string ToHtml(string markdown);
}

public interface IEmailSender
{
    Task SendAsync(string subject, string receiver, string htmlContent, CancellationToken ct = default);
}

public interface ISystemOptionService
{
    string Get(string key, string fallback = "");
    bool GetBool(string key, bool fallback = false);
    int GetInt(string key, int fallback = 0);
    IReadOnlyDictionary<string, string> Snapshot();
    Task InitAsync(CancellationToken ct = default);
    Task UpdateAsync(string key, string value, CancellationToken ct = default);
}

public interface IRateLimiter
{
    bool Check(string key, int maxRequestNum, int durationSeconds);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    long UnixSeconds { get; }
    long UnixMilliseconds { get; }
}

public interface IGjson
{
    string GetString(string json, string path);
}

public interface IChannelProvider
{
    string Type { get; }
    Task SendAsync(Message message, User user, Channel channel, CancellationToken ct);
}

public interface ITokenStore
{
    string GetToken(string key);
    Task AddChannelAsync(Channel channel, CancellationToken ct = default);
    Task RemoveChannelAsync(Channel channel, CancellationToken ct = default);
    Task UpdateChannelAsync(Channel newChannel, Channel oldChannel, CancellationToken ct = default);
    Task AddUserAsync(User user, CancellationToken ct = default);
    Task RemoveUserAsync(User user, CancellationToken ct = default);
}

public interface ISseBroker
{
    Task SubscribeAsync(int userId, Stream responseStream, CancellationToken ct);
    void Publish(int userId, Message message);
}

public interface IWebSocketClientManager
{
    Task RegisterAsync(string channelName, int userId, System.Net.WebSockets.WebSocket socket, CancellationToken ct);
    Task SendAsync(Message message, User user, Channel channel, CancellationToken ct);
}

public interface IAppCounters
{
    int MessageCount { get; }
    int UserCount { get; }
    long StartTimeUnix { get; }
    void IncrementMessage();
    void IncrementUser();
}
