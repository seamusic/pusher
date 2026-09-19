using System.Text;
using System.Text.Json;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Json;
using MessagePusher.Domain;
using MessagePusher.Domain.Exceptions;

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

    // 所有权：读取失败时由本方法释放 resp；正常返回后由调用方释放 resp。
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
        string text;
        try
        {
            text = await resp.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            resp.Dispose();
            throw;
        }
        return (resp, text);
    }

    // 所有权同 PostRawAsync：读取失败释放，正常返回由调用方释放。
    public static async Task<(HttpResponseMessage resp, string body)> PostFormAsync(HttpClient client, string url, IEnumerable<KeyValuePair<string, string>> fields, CancellationToken ct, Dictionary<string, string>? headers = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new FormUrlEncodedContent(fields);
        if (headers is not null)
        {
            foreach (var kv in headers)
                req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }
        var resp = await client.SendAsync(req, ct);
        string text;
        try
        {
            text = await resp.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            resp.Dispose();
            throw;
        }
        return (resp, text);
    }

    // 高层封装：resp 全程由本方法持有并释放，成功时返回响应体文本，
    // 非 2xx 在业务反序列化前转为可读 BusinessException；业务 code/成功语义由 Provider 判断。
    // sensitiveValues 中的凭证不会出现在异常消息里。
    public static async Task<string> SendAndReadAsync(HttpClient client, HttpMethod method, string url, HttpContent? content, string channelLabel, CancellationToken ct, Dictionary<string, string>? headers = null, IEnumerable<string?>? sensitiveValues = null)
    {
        var sensitive = sensitiveValues?.Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToArray() ?? [];
        using var req = new HttpRequestMessage(method, url);
        req.Content = content;
        if (headers is not null)
        {
            foreach (var kv in headers)
                req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }

        HttpResponseMessage resp;
        try
        {
            resp = await client.SendAsync(req, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new BusinessException($"{channelLabel} 请求失败：{SecretMask.Redact(ex.Message, sensitive)}");
        }

        using (resp)
        {
            string body;
            try
            {
                body = await resp.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new BusinessException($"{channelLabel} 读取响应失败：HTTP {(int)resp.StatusCode}");
            }

            if (!resp.IsSuccessStatusCode)
                throw new BusinessException(BuildHttpError(channelLabel, resp, body, sensitive));
            return body;
        }
    }

    public static string BuildHttpError(string channelLabel, HttpResponseMessage resp, string body, string[]? sensitive = null)
    {
        var excerpt = SecretMask.Redact(StripToPlainText(body), sensitive ?? []);
        if (excerpt.Length > 200)
            excerpt = excerpt[..200] + "…";
        var status = $"HTTP {(int)resp.StatusCode}";
        if (!string.IsNullOrEmpty(resp.ReasonPhrase))
            status += $" {resp.ReasonPhrase}";
        return string.IsNullOrWhiteSpace(excerpt)
            ? $"{channelLabel} 调用失败：{status}"
            : $"{channelLabel} 调用失败：{status}，响应：{excerpt}";
    }

    // HTML/富文本错误页转为单行纯文本摘要，避免异常消息被整页 HTML 淹没。
    public static string StripToPlainText(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "";
        var text = body;
        if (text.Contains('<'))
        {
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<(script|style)[^>]*>[\s\S]*?</\1>", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
        }
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    }
}

internal static class OutboundUrlPolicy
{
    // 出站地址校验：默认仅 HTTPS；ChannelUrlAllowNonHttps（或同名环境变量）明确开启时允许 HTTP；
    // 禁止指向本服务地址。默认服务器地址由 Provider 在调用前补齐。
    public static Uri Validate(string url, ISystemOptionService options, string channelLabel)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new BusinessException($"{channelLabel} 地址不能为空");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new BusinessException($"{channelLabel} 地址无效，必须为 http/https 绝对地址");

        var allowHttp = string.Equals(Environment.GetEnvironmentVariable("CHANNEL_URL_ALLOW_NON_HTTPS"), "true", StringComparison.OrdinalIgnoreCase)
                        || options.GetBool("ChannelUrlAllowNonHttps");
        if (uri.Scheme == Uri.UriSchemeHttp && !allowHttp)
            throw new BusinessException($"{channelLabel}必须使用 HTTPS 协议，或在系统设置中开启 ChannelUrlAllowNonHttps");

        var server = options.Get("ServerAddress", AppDefaults.ServerAddress);
        if (!string.IsNullOrWhiteSpace(server) && url.StartsWith(server, StringComparison.OrdinalIgnoreCase))
            throw new BusinessException($"{channelLabel}不能使用本服务地址");

        return uri;
    }

    // Url 为空时补 Provider 默认服务器地址（覆盖 API 直建、未经前端默认值的配置）。
    public static string WithDefault(string configuredUrl, string defaultServer) =>
        string.IsNullOrWhiteSpace(configuredUrl) ? defaultServer : configuredUrl.TrimEnd('/');
}

internal static class SecretMask
{
    public const string Placeholder = "***";

    // 将文本中出现的凭证原样替换为占位符，用于错误消息与日志脱敏。
    public static string Redact(string text, params string?[] secrets)
    {
        foreach (var secret in secrets)
        {
            if (!string.IsNullOrEmpty(secret))
                text = text.Replace(secret, Placeholder, StringComparison.Ordinal);
        }
        return text;
    }

    // 日志展示用短掩码：保留前 4 位以便排障对账，其余隐藏。
    public static string MaskKey(string secret) =>
        string.IsNullOrEmpty(secret) ? "" : secret.Length <= 8 ? Placeholder : secret[..4] + Placeholder;
}
