using MessagePusher.Application.Abstractions;
using MessagePusher.Domain;

namespace MessagePusher.Application.Services;

public sealed class VerificationService
{
    private readonly Dictionary<string, (string Code, DateTime Time)> _map = new();
    private readonly object _lock = new();
    private readonly IGuidGenerator _guid;

    public VerificationService(IGuidGenerator guid) => _guid = guid;

    public string Generate(int length)
    {
        var code = _guid.NewN();
        return length == 0 ? code : code[..Math.Min(length, code.Length)];
    }

    public void Register(string key, string code, string purpose)
    {
        lock (_lock)
        {
            _map[purpose + key] = (code, DateTime.UtcNow);
            if (_map.Count > AppDefaults.VerificationMapMaxSize)
                RemoveExpired();
        }
    }

    public bool Verify(string key, string code, string purpose)
    {
        lock (_lock)
        {
            if (!_map.TryGetValue(purpose + key, out var value))
                return false;
            if ((DateTime.UtcNow - value.Time).TotalSeconds >= AppDefaults.VerificationValidMinutes * 60)
                return false;
            return code == value.Code;
        }
    }

    public void Delete(string key, string purpose)
    {
        lock (_lock)
            _map.Remove(purpose + key);
    }

    private void RemoveExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var key in _map.Keys.ToList())
        {
            if ((now - _map[key].Time).TotalSeconds >= AppDefaults.VerificationValidMinutes * 60)
                _map.Remove(key);
        }
    }
}
