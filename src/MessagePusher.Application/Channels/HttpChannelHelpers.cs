using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MessagePusher.Application.Json;

namespace MessagePusher.Application.Channels;

internal static class HttpChannelHelpers
{
    public static async Task<T?> PostJsonAsync<T>(HttpClient client, string url, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonDefaults.Options);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await client.PostAsync(url, content, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text))
            return default;
        return JsonSerializer.Deserialize<T>(text, JsonDefaults.Options);
    }

    public static async Task<(HttpResponseMessage resp, string body)> PostRawAsync(HttpClient client, string url, object body, CancellationToken ct, Dictionary<string, string>? headers = null)
    {
        var json = body as string ?? JsonSerializer.Serialize(body, JsonDefaults.Options);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (headers is not null)
        {
            foreach (var kv in headers)
                req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }
        var resp = await client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        return (resp, text);
    }
}
