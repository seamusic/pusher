using MessagePusher.Application.Abstractions;
using MessagePusher.Domain;

namespace MessagePusher.Infrastructure.Crypto;

public sealed class BcryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 10);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            return false;
        }
    }
}

public sealed class GuidGenerator : IGuidGenerator
{
    public string NewN() => Guid.NewGuid().ToString("N");
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public long UnixSeconds => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long UnixMilliseconds => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public sealed class AppCounters : IAppCounters
{
    private int _messages;
    private int _users;
    public int MessageCount => _messages;
    public int UserCount => _users;
    public long StartTimeUnix { get; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public void IncrementMessage() => Interlocked.Increment(ref _messages);
    public void IncrementUser() => Interlocked.Increment(ref _users);
    public void SetUserCount(int n) => Interlocked.Exchange(ref _users, n);
    public void SetMessageCount(int n) => Interlocked.Exchange(ref _messages, n);
}
