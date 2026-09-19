using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Services;

public sealed class MessageService
{
    private readonly IMessageRepository _messages;
    private readonly IUserRepository _users;
    private readonly IChannelRepository _channels;
    private readonly PushService _push;

    public MessageService(IMessageRepository messages, IUserRepository users, IChannelRepository channels, PushService push)
    {
        _messages = messages;
        _users = users;
        _channels = channels;
        _push = push;
    }

    public Task<IReadOnlyList<Message>> ListAsync(int userId, int page, CancellationToken ct) =>
        _messages.GetByUserIdAsync(userId, page * AppDefaults.ItemsPerPage, AppDefaults.ItemsPerPage, ct);

    public Task<IReadOnlyList<Message>> SearchAsync(int userId, string keyword, CancellationToken ct) =>
        _messages.SearchAsync(userId, keyword, ct);

    public async Task<Message> GetAsync(int id, int userId, CancellationToken ct) =>
        await _messages.GetByIdsAsync(id, userId, ct) ?? throw new BusinessException("消息不存在");

    public Task<int> GetStatusByLinkAsync(string link, CancellationToken ct) =>
        _messages.GetStatusByLinkAsync(link, ct);

    public async Task ResendAsync(int id, int userId, CancellationToken ct)
    {
        var message = await _messages.GetByIdsAsync(id, userId, ct) ?? throw new BusinessException("消息不存在");
        message.Id = 0;
        var user = await _users.GetByIdAsync(userId, true, ct) ?? throw new BusinessException("用户不存在");
        var channel = await _channels.GetByNameAsync(message.Channel, user.Id, ct)
                      ?? throw new BusinessException("通道不存在");
        await _push.SaveAndSendAsync(user, message, channel, ct);
    }

    public Task DeleteAsync(int id, int userId, CancellationToken ct) => _messages.DeleteAsync(id, userId, ct);

    public Task DeleteAllAsync(CancellationToken ct) => _messages.DeleteAllAsync(ct);

    public Task<Message?> GetByLinkAsync(string link, CancellationToken ct) => _messages.GetByLinkAsync(link, ct);
}
