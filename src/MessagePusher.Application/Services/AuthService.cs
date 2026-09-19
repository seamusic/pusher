using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Services;

public sealed class AuthService
{
    private readonly IUserRepository _users;
    private readonly IChannelRepository _channels;
    private readonly IPasswordHasher _hasher;
    private readonly ISystemOptionService _options;
    private readonly VerificationService _verification;
    private readonly IEmailSender _email;
    private readonly IHttpClientFactory _http;
    private readonly IClock _clock;
    private readonly IAppCounters _counters;

    public AuthService(
        IUserRepository users,
        IChannelRepository channels,
        IPasswordHasher hasher,
        ISystemOptionService options,
        VerificationService verification,
        IEmailSender email,
        IHttpClientFactory http,
        IClock clock,
        IAppCounters counters)
    {
        _users = users;
        _channels = channels;
        _hasher = hasher;
        _options = options;
        _verification = verification;
        _email = email;
        _http = http;
        _clock = clock;
        _counters = counters;
    }

    public async Task<User> LoginAsync(string username, string password, CancellationToken ct)
    {
        if (!_options.GetBool("PasswordLoginEnabled", true))
            throw new BusinessException("管理员关闭了密码登录");
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            throw new BusinessException("无效的参数");
        var user = await _users.GetByUsernameAsync(username, ct);
        if (user is null || user.Id == 0 || !_hasher.Verify(password, user.Password) || user.Status != (int)UserStatus.Enabled)
            throw new BusinessException("用户名或密码错误，或用户已被封禁");
        return user;
    }

    public async Task RegisterAsync(User input, CancellationToken ct)
    {
        if (!_options.GetBool("RegisterEnabled", true))
            throw new BusinessException("管理员关闭了新用户注册");
        if (!_options.GetBool("PasswordRegisterEnabled", true))
            throw new BusinessException("管理员关闭了通过密码进行注册，请使用第三方账户验证的形式进行注册");
        ValidateUser(input);
        if (_options.GetBool("EmailVerificationEnabled"))
        {
            if (string.IsNullOrEmpty(input.Email) || string.IsNullOrEmpty(input.VerificationCode))
                throw new BusinessException("管理员开启了邮箱验证，请输入邮箱地址和验证码");
            if (!_verification.Verify(input.Email, input.VerificationCode, VerificationPurpose.Email))
                throw new BusinessException("验证码错误或已过期");
        }
        var clean = new User
        {
            Username = input.Username,
            Password = _hasher.Hash(input.Password),
            DisplayName = input.Username,
            Role = Roles.Common,
            Status = (int)UserStatus.Enabled
        };
        if (_options.GetBool("EmailVerificationEnabled"))
            clean.Email = input.Email;
        await _users.AddAsync(clean, ct);
        _counters.IncrementUser();
    }

    public async Task SendEmailVerificationAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new BusinessException("无效的参数");
        if (await _users.EmailTakenAsync(email, ct))
            throw new BusinessException("邮箱地址已被占用");
        var code = _verification.Generate(6);
        _verification.Register(email, code, VerificationPurpose.Email);
        var name = _options.Get("SystemName", AppDefaults.SystemName);
        var subject = $"{name}邮箱验证邮件";
        var content = $"<p>您好，你正在进行{name}邮箱验证。</p>" +
                      $"<p>您的验证码为: <strong>{code}</strong></p>" +
                      $"<p>验证码 {AppDefaults.VerificationValidMinutes} 分钟内有效，如果不是本人操作，请忽略。</p>";
        await _email.SendAsync(subject, email, content, ct);
    }

    public async Task SendPasswordResetEmailAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new BusinessException("无效的参数");
        if (!await _users.EmailTakenAsync(email, ct))
            throw new BusinessException("该邮箱地址未注册");
        var code = _verification.Generate(0);
        _verification.Register(email, code, VerificationPurpose.PasswordReset);
        var server = _options.Get("ServerAddress", AppDefaults.ServerAddress);
        var link = $"{server}/user/reset?email={email}&token={code}";
        var name = _options.Get("SystemName", AppDefaults.SystemName);
        var subject = $"{name}密码重置";
        var content = $"<p>您好，你正在进行{name}密码重置。</p>" +
                      $"<p>点击<a href='{link}'>此处</a>进行密码重置。</p>" +
                      $"<p>重置链接 {AppDefaults.VerificationValidMinutes} 分钟内有效，如果不是本人操作，请忽略。</p>";
        await _email.SendAsync(subject, email, content, ct);
    }

    public async Task<string> ResetPasswordAsync(string email, string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            throw new BusinessException("无效的参数");
        if (!_verification.Verify(email, token, VerificationPurpose.PasswordReset))
            throw new BusinessException("重置链接非法或已过期");
        var password = _verification.Generate(12);
        await _users.ResetPasswordByEmailAsync(email, _hasher.Hash(password), ct);
        _verification.Delete(email, VerificationPurpose.PasswordReset);
        return password;
    }

    public async Task BindEmailAsync(int userId, string email, string code, CancellationToken ct)
    {
        if (!_verification.Verify(email, code, VerificationPurpose.Email))
            throw new BusinessException("验证码错误或已过期");
        var user = await _users.GetByIdAsync(userId, true, ct) ?? throw new BusinessException("用户不存在");
        user.Email = email;
        await _users.UpdateFieldsAsync(user, ct);
        await _channels.AddAsync(new Channel
        {
            Type = ChannelType.Email,
            UserId = user.Id,
            Name = "email",
            Description = "系统自动创建",
            Status = (int)ChannelStatus.Enabled,
            CreatedTime = _clock.UnixSeconds
        }, ct);
    }

    public async Task<User> GitHubOAuthAsync(string code, int? sessionUserId, CancellationToken ct)
    {
        if (sessionUserId is not null)
        {
            await GitHubBindAsync(sessionUserId.Value, code, ct);
            throw new BindCompleteException();
        }
        if (!_options.GetBool("GitHubOAuthEnabled"))
            throw new BusinessException("管理员未开启通过 GitHub 登录以及注册");
        var gh = await GetGitHubUserAsync(code, ct);
        var user = await _users.GetByGitHubIdAsync(gh.Login, ct);
        if (user is not null && user.Id != 0)
        {
            if (user.Status != (int)UserStatus.Enabled)
                throw new BusinessException("用户已被封禁");
            return user;
        }
        if (!_options.GetBool("RegisterEnabled", true))
            throw new BusinessException("管理员关闭了新用户注册");
        var max = await _users.GetMaxIdAsync(ct);
        user = new User
        {
            Username = "github_" + (max + 1),
            DisplayName = string.IsNullOrEmpty(gh.Name) ? "GitHub User" : gh.Name,
            Email = gh.Email ?? "",
            GitHubId = gh.Login,
            Role = Roles.Common,
            Status = (int)UserStatus.Enabled,
            Password = _hasher.Hash(_verification.Generate(16))
        };
        await _users.AddAsync(user, ct);
        _counters.IncrementUser();
        return user;
    }

    public async Task GitHubBindAsync(int userId, string code, CancellationToken ct)
    {
        if (!_options.GetBool("GitHubOAuthEnabled"))
            throw new BusinessException("管理员未开启通过 GitHub 登录以及注册");
        var gh = await GetGitHubUserAsync(code, ct);
        if (await _users.GitHubIdTakenAsync(gh.Login, ct))
            throw new BusinessException("该 GitHub 账户已被绑定");
        var user = await _users.GetByIdAsync(userId, true, ct) ?? throw new BusinessException("用户不存在");
        user.GitHubId = gh.Login;
        await _users.UpdateFieldsAsync(user, ct);
    }

    public async Task<User> WeChatAuthAsync(string code, CancellationToken ct)
    {
        if (!_options.GetBool("WeChatAuthEnabled"))
            throw new BusinessException("管理员未开启通过微信登录以及注册");
        var wechatId = await GetWeChatIdAsync(code, ct);
        var user = await _users.GetByWeChatIdAsync(wechatId, ct);
        if (user is not null && user.Id != 0)
        {
            if (user.Status != (int)UserStatus.Enabled)
                throw new BusinessException("用户已被封禁");
            return user;
        }
        if (!_options.GetBool("RegisterEnabled", true))
            throw new BusinessException("管理员关闭了新用户注册");
        var max = await _users.GetMaxIdAsync(ct);
        user = new User
        {
            Username = "wechat_" + (max + 1),
            DisplayName = "WeChat User",
            WeChatId = wechatId,
            Role = Roles.Common,
            Status = (int)UserStatus.Enabled,
            Password = _hasher.Hash(_verification.Generate(16))
        };
        await _users.AddAsync(user, ct);
        _counters.IncrementUser();
        return user;
    }

    public async Task WeChatBindAsync(int userId, string code, CancellationToken ct)
    {
        if (!_options.GetBool("WeChatAuthEnabled"))
            throw new BusinessException("管理员未开启通过微信登录以及注册");
        var wechatId = await GetWeChatIdAsync(code, ct);
        if (await _users.WeChatIdTakenAsync(wechatId, ct))
            throw new BusinessException("该微信账号已被绑定");
        var user = await _users.GetByIdAsync(userId, true, ct) ?? throw new BusinessException("用户不存在");
        user.WeChatId = wechatId;
        await _users.UpdateFieldsAsync(user, ct);
    }

    private async Task<GitHubUser> GetGitHubUserAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code))
            throw new BusinessException("无效的参数");
        var client = _http.CreateClient("oauth");
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
        req.Content = JsonContent.Create(new
        {
            client_id = _options.Get("GitHubClientId"),
            client_secret = _options.Get("GitHubClientSecret"),
            code
        });
        req.Headers.Accept.ParseAdd("application/json");
        using var res = await client.SendAsync(req, ct);
        var token = await res.Content.ReadFromJsonAsync<GitHubOAuthResponse>(ct);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
            throw new BusinessException("无法连接至 GitHub 服务器，请稍后重试！");
        using var userReq = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        userReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
        userReq.Headers.UserAgent.ParseAdd("message-pusher");
        using var userRes = await client.SendAsync(userReq, ct);
        var gh = await userRes.Content.ReadFromJsonAsync<GitHubUser>(ct);
        if (gh is null || string.IsNullOrEmpty(gh.Login))
            throw new BusinessException("返回值非法，用户字段为空，请稍后重试！");
        return gh;
    }

    private async Task<string> GetWeChatIdAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code))
            throw new BusinessException("无效的参数");
        var addr = _options.Get("WeChatServerAddress");
        var client = _http.CreateClient("oauth");
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{addr}/api/wechat/user?code={code}");
        req.Headers.TryAddWithoutValidation("Authorization", _options.Get("WeChatServerToken"));
        using var res = await client.SendAsync(req, ct);
        var body = await res.Content.ReadFromJsonAsync<WeChatLoginResponse>(ct);
        if (body is null)
            throw new BusinessException("验证码错误或已过期");
        if (!body.Success)
            throw new BusinessException(body.Message ?? "");
        if (string.IsNullOrEmpty(body.Data))
            throw new BusinessException("验证码错误或已过期");
        return body.Data;
    }

    private static void ValidateUser(User user)
    {
        if (string.IsNullOrEmpty(user.Username) || user.Username.Length > 12)
            throw new BusinessException("输入不合法 Username");
        if (string.IsNullOrEmpty(user.Password) || user.Password.Length < 8 || user.Password.Length > 20)
            throw new BusinessException("输入不合法 Password");
        if (!string.IsNullOrEmpty(user.DisplayName) && user.DisplayName.Length > 20)
            throw new BusinessException("输入不合法 DisplayName");
    }

    private sealed class GitHubOAuthResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    }

    private sealed class GitHubUser
    {
        public string Login { get; set; } = "";
        public string? Name { get; set; }
        public string? Email { get; set; }
    }

    private sealed class WeChatLoginResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Data { get; set; }
    }
}

public sealed class BindCompleteException : Exception
{
    public BindCompleteException() : base("bind")
    {
    }
}
