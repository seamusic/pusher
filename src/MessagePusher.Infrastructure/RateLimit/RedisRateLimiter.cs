using MessagePusher.Application.Abstractions;
using MessagePusher.Domain;
using StackExchange.Redis;

namespace MessagePusher.Infrastructure.RateLimit;

public sealed class RedisRateLimiter : IRateLimiter
{
    private readonly IDatabase _db;
    public RedisRateLimiter(IConnectionMultiplexer mux) => _db = mux.GetDatabase();

    public bool Check(string key, int maxRequestNum, int durationSeconds)
    {
        var redisKey = "rateLimit:" + key;
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var expire = TimeSpan.FromMinutes(RateLimitDefaults.KeyExpirationMinutes);
        var listLength = _db.ListLength(redisKey);
        if (listLength < maxRequestNum)
        {
            _db.ListLeftPush(redisKey, now);
            _db.KeyExpire(redisKey, expire);
            return true;
        }
        var oldTimeStr = _db.ListGetByIndex(redisKey, -1).ToString();
        if (!DateTime.TryParse(oldTimeStr, out var oldTime))
            oldTime = DateTime.MinValue;
        var nowTime = DateTime.Parse(now);
        if ((nowTime - oldTime).TotalSeconds < durationSeconds)
        {
            _db.KeyExpire(redisKey, expire);
            return false;
        }
        _db.ListLeftPush(redisKey, now);
        _db.ListTrim(redisKey, 0, maxRequestNum - 1);
        _db.KeyExpire(redisKey, expire);
        return true;
    }
}
