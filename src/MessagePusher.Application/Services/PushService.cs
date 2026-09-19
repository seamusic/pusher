using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Application.Channels;
using MessagePusher.Application.Messaging;
using MessagePusher.Application.Results;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Services;

public sealed class PushService
{
    private readonly IUserRepository _users;
    private readonly IChannelRepository _channels;
    private readonly IMessageRepository _messages;
    private readonly ISystemOptionService _options;
    private readonly IGuidGenerator _guid;
    private readonly IClock _clock;
    private readonly ChannelProviderFactory _factory;
    private readonly AsyncMessageQueue _queue;
    private readonly ISseBroker _sse;
    private readonly IAppCounters _counters;

    public PushService(
        IUserRepository users,
        IChannelRepository channels,
        IMessageRepository messages,
        ISystemOptionService options,
        IGuidGenerator guid,
        IClock clock,
        ChannelProviderFactory factory,
        AsyncMessageQueue queue,
        ISseBroker sse,
        IAppCounters counters)
    {
        _users = users;
        _channels = channels;
        _messages = messages;
        _options = options;
        _guid = guid;
        _clock = clock;
        _factory = factory;
        _queue = queue;
        _sse = sse;
        _counters = counters;
    }

    public static void KeepCompatible(Message message)
    {
        if (string.IsNullOrEmpty(message.Description))
            message.Description = message.Short ?? "";
        if (string.IsNullOrEmpty(message.Content))
            message.Content = message.Desp ?? "";
        if (string.IsNullOrEmpty(message.To))
            message.To = message.OpenId ?? "";
    }

    public static bool AuthMessage(string messageToken, string userToken, string? channelToken)
    {
        if (!string.IsNullOrEmpty(userToken))
            return messageToken == userToken;
        if (!string.IsNullOrEmpty(channelToken))
            return messageToken == channelToken;
        return true;
    }

    public async Task<PushResult> PushAsync(string username, Message message, bool needAuth, CancellationToken ct)
    {
        KeepCompatible(message);
        var user = await _users.GetByUsernameAsync(username, ct);
        if (user is null || user.Status == (int)UserStatus.NonExisted)
            throw new BusinessException("用户不存在");
        if (user.Id == 0 && user.Username == "")
            throw new BusinessException("用户不存在");
        if (user.Status == (int)UserStatus.Disabled)
            throw new BusinessException("用户已被封禁");
        if (user.Id == 0)
            throw new BusinessException("用户不存在");

        await ProcessAsync(message, user, needAuth, ct);
        return PushResult.Ok(message.Link);
    }

    public async Task ProcessAsync(Message message, User user, bool needAuth, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(message.Title))
            message.Title = _options.Get("SystemName", AppDefaults.SystemName);
        if (string.IsNullOrEmpty(message.Channel))
        {
            message.Channel = user.Channel;
            if (string.IsNullOrEmpty(message.Channel))
                message.Channel = ChannelType.Email;
        }

        var channel = await _channels.GetByNameAsync(message.Channel, user.Id, ct)
                      ?? throw new BusinessException("无效的渠道名称：" + message.Channel);

        if (needAuth && !AuthMessage(message.Token ?? "", user.Token, channel.Token))
        {
            if (string.IsNullOrEmpty(message.Token))
                throw new UnauthorizedException("通道维度或用户维度设置了鉴权令牌，需要提供鉴权令牌");
            throw new UnauthorizedException("无效的 token");
        }

        if (message.RenderMode == "code" && !string.IsNullOrEmpty(message.Content))
            message.Content = $"```\n{message.Content}\n```";

        await SaveAndSendAsync(user, message, channel, ct);
    }

    public async Task SaveAndSendAsync(User user, Message message, Channel channel, CancellationToken ct)
    {
        if (channel.Status != (int)ChannelStatus.Enabled)
            throw new BusinessException("该渠道已被禁用");

        _counters.IncrementMessage();
        message.Link = _guid.NewN();
        if (string.IsNullOrEmpty(message.Url))
            message.Url = $"{_options.Get("ServerAddress", AppDefaults.ServerAddress)}/message/{message.Link}";

        var persist = _options.GetBool("MessagePersistenceEnabled", true)
                      || user.SaveMessageToDatabase == UserPreference.Allowed;
        var success = false;
        try
        {
            if (persist)
            {
                message.Timestamp = _clock.UnixSeconds;
                message.UserId = user.Id;
                message.Status = (int)MessageSendStatus.Pending;
                await _messages.AddAsync(message, ct);
                _sse.Publish(user.Id, message);
            }
            else
            {
                if (message.Async)
                    throw new BusinessException("异步发送消息需要用户具备消息持久化的权限");
                message.Link = "unsaved";
                _sse.Publish(user.Id, message);
            }

            if (!message.Async)
                await _factory.Resolve(channel.Type).SendAsync(message, user, channel, ct);
            success = true;
        }
        finally
        {
            if (persist)
            {
                var status = message.Async
                    ? (int)MessageSendStatus.AsyncPending
                    : success ? (int)MessageSendStatus.Sent : (int)MessageSendStatus.Failed;
                try
                {
                    await _messages.UpdateStatusAsync(message, status, ct);
                }
                catch
                {
                    // logged by caller
                }
                if (message.Async && success)
                    await _queue.EnqueueAsync(message.Id, ct);
            }
        }
    }
}
