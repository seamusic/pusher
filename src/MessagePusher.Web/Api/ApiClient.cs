using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MessagePusher.Web.Api;

public sealed class ApiClient
{
    public static readonly JsonSerializerOptions Json = new()
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

    public Task<ApiResult<object?>> UpdateSelfAsync(object body, CancellationToken ct = default) =>
        PutAsync<object?>("api/user/self", body, ct);

    public Task<ApiResult<string?>> GenerateTokenAsync(CancellationToken ct = default) =>
        GetAsync<string?>("api/user/token", ct);

    public Task<ApiResult<List<UserInfo>?>> ListUsersAsync(int page, CancellationToken ct = default) =>
        GetAsync<List<UserInfo>?>($"api/user/?p={page}", ct);

    public Task<ApiResult<List<UserInfo>?>> SearchUsersAsync(string keyword, CancellationToken ct = default) =>
        GetAsync<List<UserInfo>?>($"api/user/search?keyword={Uri.EscapeDataString(keyword)}", ct);

    public Task<ApiResult<UserInfo?>> GetUserAsync(int id, CancellationToken ct = default) =>
        GetAsync<UserInfo?>($"api/user/{id}", ct);

    public Task<ApiResult<object?>> CreateUserAsync(object body, CancellationToken ct = default) =>
        PostAsync<object?>("api/user/", body, ct);

    public Task<ApiResult<object?>> UpdateUserAsync(object body, CancellationToken ct = default) =>
        PutAsync<object?>("api/user/", body, ct);

    public Task<ApiResult<UserInfo?>> ManageUserAsync(string username, string action, CancellationToken ct = default) =>
        PostAsync<UserInfo?>("api/user/manage", new { username, action }, ct);

    public Task<ApiResult<object?>> DeleteUserAsync(int id, CancellationToken ct = default) =>
        DeleteAsync<object?>($"api/user/{id}", ct);

    public Task<ApiResult<StatusInfo?>> StatusAsync(CancellationToken ct = default) =>
        GetAsync<StatusInfo?>("api/status", ct);

    public Task<ApiResult<string?>> NoticeAsync(CancellationToken ct = default) =>
        GetAsync<string?>("api/notice", ct);

    public Task<ApiResult<string?>> AboutAsync(CancellationToken ct = default) =>
        GetAsync<string?>("api/about", ct);

    public Task<ApiResult<object?>> BindEmailAsync(string email, string code, CancellationToken ct = default) =>
        GetAsync<object?>($"api/oauth/email/bind?email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(code)}", ct);

    public Task<ApiResult<object?>> SendVerificationAsync(string email, CancellationToken ct = default) =>
        GetAsync<object?>($"api/verification?email={Uri.EscapeDataString(email)}", ct);

    public Task<ApiResult<List<MessageDto>?>> ListMessagesAsync(int page, CancellationToken ct = default) =>
        GetAsync<List<MessageDto>?>($"api/message/?p={page}", ct);

    public Task<ApiResult<List<MessageDto>?>> SearchMessagesAsync(string keyword, CancellationToken ct = default) =>
        GetAsync<List<MessageDto>?>($"api/message/search?keyword={Uri.EscapeDataString(keyword)}", ct);

    public Task<ApiResult<MessageDto?>> GetMessageAsync(int id, CancellationToken ct = default) =>
        GetAsync<MessageDto?>($"api/message/{id}", ct);

    public Task<ApiResult<object?>> ResendMessageAsync(int id, CancellationToken ct = default) =>
        PostAsync<object?>($"api/message/resend/{id}", new { }, ct);

    public Task<ApiResult<object?>> DeleteMessageAsync(int id, CancellationToken ct = default) =>
        DeleteAsync<object?>($"api/message/{id}", ct);

    public Task<ApiResult<List<ChannelDto>?>> ListChannelsAsync(int page, CancellationToken ct = default) =>
        GetAsync<List<ChannelDto>?>($"api/channel/?p={page}", ct);

    public Task<ApiResult<List<BriefChannelDto>?>> BriefChannelsAsync(CancellationToken ct = default) =>
        GetAsync<List<BriefChannelDto>?>("api/channel/?brief=1", ct);

    public Task<ApiResult<List<ChannelDto>?>> SearchChannelsAsync(string keyword, CancellationToken ct = default) =>
        GetAsync<List<ChannelDto>?>($"api/channel/search?keyword={Uri.EscapeDataString(keyword)}", ct);

    public Task<ApiResult<ChannelDto?>> GetChannelAsync(int id, CancellationToken ct = default) =>
        GetAsync<ChannelDto?>($"api/channel/{id}", ct);

    public Task<ApiResult<object?>> AddChannelAsync(object body, CancellationToken ct = default) =>
        PostAsync<object?>("api/channel/", body, ct);

    public Task<ApiResult<ChannelDto?>> UpdateChannelAsync(object body, bool statusOnly = false, CancellationToken ct = default) =>
        PutAsync<ChannelDto?>(statusOnly ? "api/channel/?status_only=true" : "api/channel/", body, ct);

    public Task<ApiResult<object?>> DeleteChannelAsync(int id, CancellationToken ct = default) =>
        DeleteAsync<object?>($"api/channel/{id}", ct);

    public Task<ApiResult<List<WebhookDto>?>> ListWebhooksAsync(int page, CancellationToken ct = default) =>
        GetAsync<List<WebhookDto>?>($"api/webhook/?p={page}", ct);

    public Task<ApiResult<List<WebhookDto>?>> SearchWebhooksAsync(string keyword, CancellationToken ct = default) =>
        GetAsync<List<WebhookDto>?>($"api/webhook/search?keyword={Uri.EscapeDataString(keyword)}", ct);

    public Task<ApiResult<WebhookDto?>> GetWebhookAsync(int id, CancellationToken ct = default) =>
        GetAsync<WebhookDto?>($"api/webhook/{id}", ct);

    public Task<ApiResult<object?>> AddWebhookAsync(object body, CancellationToken ct = default) =>
        PostAsync<object?>("api/webhook/", body, ct);

    public Task<ApiResult<WebhookDto?>> UpdateWebhookAsync(object body, bool statusOnly = false, CancellationToken ct = default) =>
        PutAsync<WebhookDto?>(statusOnly ? "api/webhook/?status_only=true" : "api/webhook/", body, ct);

    public Task<ApiResult<object?>> DeleteWebhookAsync(int id, CancellationToken ct = default) =>
        DeleteAsync<object?>($"api/webhook/{id}", ct);

    public Task<ApiResult<List<OptionDto>?>> ListOptionsAsync(CancellationToken ct = default) =>
        GetAsync<List<OptionDto>?>("api/option/", ct);

    public Task<ApiResult<object?>> UpdateOptionAsync(string key, string value, CancellationToken ct = default) =>
        PutAsync<object?>("api/option/", new { key, value }, ct);

    public async Task<PushResult> PushAsync(string username, object body, CancellationToken ct = default)
    {
        try
        {
            using var res = await _http.PostAsJsonAsync($"push/{Uri.EscapeDataString(username)}", body, Json, ct);
            var parsed = await res.Content.ReadFromJsonAsync<PushResult>(Json, ct);
            return parsed ?? new PushResult { Success = false, Message = "无效响应" };
        }
        catch (HttpRequestException)
        {
            return new PushResult { Success = false, Message = "无法连接 API" };
        }
        catch (TaskCanceledException)
        {
            return new PushResult { Success = false, Message = "无法连接 API" };
        }
    }

    private Task<ApiResult<T>> GetAsync<T>(string url, CancellationToken ct) => SendAsync<T>(() => _http.GetAsync(url, ct), ct);

    private Task<ApiResult<T>> PostAsync<T>(string url, object body, CancellationToken ct) =>
        SendAsync<T>(() => _http.PostAsJsonAsync(url, body, Json, ct), ct);

    private Task<ApiResult<T>> PutAsync<T>(string url, object body, CancellationToken ct) =>
        SendAsync<T>(() => _http.PutAsJsonAsync(url, body, Json, ct), ct);

    private Task<ApiResult<T>> DeleteAsync<T>(string url, CancellationToken ct) =>
        SendAsync<T>(() => _http.DeleteAsync(url, ct), ct);

    private static async Task<ApiResult<T>> SendAsync<T>(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        try
        {
            using var res = await send();
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
