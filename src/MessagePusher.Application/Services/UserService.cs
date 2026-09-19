using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Application.Policies;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Services;

public sealed class UserService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IGuidGenerator _guid;
    private readonly ITokenStore _tokens;
    private readonly UserPolicy _policy;
    private readonly IAppCounters _counters;

    public UserService(IUserRepository users, IPasswordHasher hasher, IGuidGenerator guid, ITokenStore tokens, UserPolicy policy, IAppCounters counters)
    {
        _users = users;
        _hasher = hasher;
        _guid = guid;
        _tokens = tokens;
        _policy = policy;
        _counters = counters;
    }

    public Task<IReadOnlyList<User>> ListAsync(int page, CancellationToken ct) =>
        _users.GetAllAsync(page * AppDefaults.ItemsPerPage, AppDefaults.ItemsPerPage, ct);

    public Task<IReadOnlyList<User>> SearchAsync(string keyword, CancellationToken ct) =>
        _users.SearchAsync(keyword, ct);

    public async Task<User> GetAsync(int id, int actorRole, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(id, false, ct) ?? throw new BusinessException("用户不存在");
        _policy.EnsureCanRead(actorRole, user);
        return user;
    }

    public Task<User?> GetSelfAsync(int id, CancellationToken ct) => _users.GetByIdAsync(id, false, ct);

    public async Task CreateAsync(User input, int actorRole, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(input.Username) || string.IsNullOrEmpty(input.Password))
            throw new BusinessException("无效的参数");
        if (string.IsNullOrEmpty(input.DisplayName))
            input.DisplayName = input.Username;
        _policy.EnsureCanCreate(actorRole, input.Role);
        var clean = new User
        {
            Username = input.Username,
            Password = _hasher.Hash(input.Password),
            DisplayName = input.DisplayName,
            Role = Roles.Common,
            Status = (int)UserStatus.Enabled
        };
        await _users.AddAsync(clean, ct);
        _counters.IncrementUser();
    }

    public async Task UpdateUserAsync(User updated, int actorRole, CancellationToken ct)
    {
        if (updated.Id == 0)
            throw new BusinessException("无效的参数");
        var origin = await _users.GetByIdAsync(updated.Id, false, ct) ?? throw new BusinessException("用户不存在");
        _policy.EnsureCanUpdate(actorRole, origin, updated.Role == 0 ? origin.Role : updated.Role);
        var updatePassword = !string.IsNullOrEmpty(updated.Password);
        var clean = new User
        {
            Id = updated.Id,
            Username = updated.Username,
            Password = updatePassword ? _hasher.Hash(updated.Password) : "",
            DisplayName = updated.DisplayName
        };
        await _users.UpdateAsync(clean, updatePassword, ct);
    }

    public async Task UpdateSelfAsync(int id, User input, CancellationToken ct)
    {
        var updatePassword = !string.IsNullOrEmpty(input.Password);
        var clean = new User
        {
            Id = id,
            Username = input.Username,
            Password = updatePassword ? _hasher.Hash(input.Password) : "",
            DisplayName = input.DisplayName,
            Token = input.Token,
            Channel = input.Channel
        };
        await _users.UpdateAsync(clean, updatePassword, ct);
    }

    public async Task DeleteAsync(int id, int actorRole, CancellationToken ct)
    {
        var origin = await _users.GetByIdAsync(id, true, ct) ?? throw new BusinessException("用户不存在");
        _policy.EnsureCanDelete(actorRole, origin);
        await _tokens.RemoveUserAsync(origin, ct);
        await _users.DeleteOwnedDataAsync(id, ct);
        await _users.DeleteAsync(id, ct);
    }

    public async Task DeleteSelfAsync(int id, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(id, true, ct) ?? throw new BusinessException("用户不存在");
        await _tokens.RemoveUserAsync(user, ct);
        await _users.DeleteOwnedDataAsync(id, ct);
        await _users.DeleteAsync(id, ct);
    }

    public async Task<string> GenerateTokenAsync(int id, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(id, true, ct) ?? throw new BusinessException("用户不存在");
        var token = _guid.NewN();
        if (await _users.TokenExistsAsync(token, ct))
            throw new BusinessException("请重试，系统生成的 UUID 竟然重复了！");
        user.Token = token;
        await _users.UpdateFieldsAsync(user, ct);
        return token;
    }

    public async Task<User> ManageAsync(string username, string action, int actorRole, CancellationToken ct)
    {
        var user = await _users.GetByUsernameAsync(username, ct);
        if (user is null || user.Id == 0)
            throw new BusinessException("用户不存在");
        _policy.EnsureCanManage(actorRole, user);
        switch (action)
        {
            case "disable":
                if (user.Status == (int)UserStatus.Disabled)
                    throw new BusinessException("该账户已经是封禁状态");
                if (user.Role == Roles.Root)
                    throw new BusinessException("无法禁用超级管理员用户");
                await _tokens.RemoveUserAsync(user, ct);
                user.Status = (int)UserStatus.Disabled;
                break;
            case "enable":
                if (user.Status == (int)UserStatus.Enabled)
                    throw new BusinessException("该账户已经是启用状态");
                await _tokens.AddUserAsync(user, ct);
                user.Status = (int)UserStatus.Enabled;
                break;
            case "delete":
                if (user.Role == Roles.Root)
                    throw new BusinessException("无法删除超级管理员用户");
                await _tokens.RemoveUserAsync(user, ct);
                await _users.DeleteOwnedDataAsync(user.Id, ct);
                await _users.DeleteAsync(user.Id, ct);
                return new User { Role = user.Role, Status = user.Status, SendEmailToOthers = user.SendEmailToOthers, SaveMessageToDatabase = user.SaveMessageToDatabase };
            case "promote":
                _policy.EnsureRootForPromote(actorRole);
                if (user.Role >= Roles.Admin)
                    throw new BusinessException("该用户已经是管理员");
                user.Role = Roles.Admin;
                break;
            case "demote":
                if (user.Role == Roles.Root)
                    throw new BusinessException("无法降级超级管理员用户");
                if (user.Role == Roles.Common)
                    throw new BusinessException("该用户已经是普通用户");
                user.Role = Roles.Common;
                break;
            case "allow_send_email_to_others":
                user.SendEmailToOthers = UserPreference.Allowed;
                break;
            case "disallow_send_email_to_others":
                user.SendEmailToOthers = UserPreference.Disallowed;
                break;
            case "allow_save_message_to_database":
                user.SaveMessageToDatabase = UserPreference.Allowed;
                break;
            case "disallow_save_message_to_database":
                user.SaveMessageToDatabase = UserPreference.Disallowed;
                break;
            default:
                throw new BusinessException("无效的参数");
        }
        await _users.UpdateFieldsAsync(user, ct);
        return new User
        {
            Role = user.Role,
            Status = user.Status,
            SendEmailToOthers = user.SendEmailToOthers,
            SaveMessageToDatabase = user.SaveMessageToDatabase
        };
    }
}
