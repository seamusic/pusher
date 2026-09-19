using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MessagePusher.Web.Api;

public sealed class ApiClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public ApiClient(HttpClient http) => _http = http;

    public Task<ApiResult<UserInfo?>> LoginAsync(string username, string password, CancellationToken ct = default) =>
        PostAsync<UserInfo?>("api/user/login", new { username, password }, ct);

    public Task<ApiResult<object?>> LogoutAsync(CancellationToken ct = default) =>
        GetAsync<object?>("api/user/logout", ct);

    public Task<ApiResult<UserInfo?>> SelfAsync(CancellationToken ct = default) =>
        GetAsync<UserInfo?>("api/user/self", ct);

    public Task<ApiResult<StatusInfo?>> StatusAsync(CancellationToken ct = default) =>
        GetAsync<StatusInfo?>("api/status", ct);

    public Task<ApiResult<string?>> NoticeAsync(CancellationToken ct = default) =>
        GetAsync<string?>("api/notice", ct);

    public Task<ApiResult<string?>> AboutAsync(CancellationToken ct = default) =>
        GetAsync<string?>("api/about", ct);

    private async Task<ApiResult<T>> GetAsync<T>(string url, CancellationToken ct)
    {
        try
        {
            using var res = await _http.GetAsync(url, ct);
            return await ReadAsync<T>(res, ct);
        }
        catch (HttpRequestException)
        {
            return new ApiResult<T> { Success = false, Message = "无法连接 API" };
        }
        catch (TaskCanceledException)
        {
            return new ApiResult<T> { Success = false, Message = "无法连接 API" };
        }
    }

    private async Task<ApiResult<T>> PostAsync<T>(string url, object body, CancellationToken ct)
    {
        try
        {
            using var res = await _http.PostAsJsonAsync(url, body, Json, ct);
            return await ReadAsync<T>(res, ct);
        }
        catch (HttpRequestException)
        {
            return new ApiResult<T> { Success = false, Message = "无法连接 API" };
        }
        catch (TaskCanceledException)
        {
            return new ApiResult<T> { Success = false, Message = "无法连接 API" };
        }
    }

    private static async Task<ApiResult<T>> ReadAsync<T>(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.StatusCode == HttpStatusCode.Unauthorized)
            return new ApiResult<T> { Success = false, Message = "未登录" };
        try
        {
            var parsed = await res.Content.ReadFromJsonAsync<ApiResult<T>>(Json, ct);
            return parsed ?? new ApiResult<T> { Success = false, Message = "无效响应" };
        }
        catch (JsonException)
        {
            return new ApiResult<T> { Success = false, Message = "无法连接 API" };
        }
    }
}
