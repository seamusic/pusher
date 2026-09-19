using MessagePusher.Application.Abstractions;

namespace MessagePusher.Infrastructure.RateLimit;

public sealed class MemoryRateLimiter : IRateLimiter, IDisposable
{
    private readonly Dictionary<string, List<long>> _map = new();
    private readonly object _lock = new();
    private readonly Timer _timer;
    private readonly TimeSpan _expiration;

    public MemoryRateLimiter(TimeSpan? expiration = null)
    {
        _expiration = expiration ?? TimeSpan.FromMinutes(20);
        _timer = new Timer(_ => ClearExpired(), null, _expiration, _expiration);
    }

    public bool Check(string key, int maxRequestNum, int durationSeconds)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_lock)
        {
            if (!_map.TryGetValue(key, out var list))
            {
                list = new List<long>();
                _map[key] = list;
            }
            if (list.Count < maxRequestNum)
            {
                list.Add(now);
                return true;
            }
            var oldest = list[0];
            if (now - oldest >= durationSeconds)
            {
                list.RemoveAt(0);
                list.Add(now);
                return true;
            }
            return false;
        }
    }

    private void ClearExpired()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_lock)
        {
            foreach (var key in _map.Keys.ToList())
            {
                var list = _map[key];
                list.RemoveAll(t => now - t > (long)_expiration.TotalSeconds);
                if (list.Count == 0)
                    _map.Remove(key);
            }
        }
    }

    public void Dispose() => _timer.Dispose();
}
