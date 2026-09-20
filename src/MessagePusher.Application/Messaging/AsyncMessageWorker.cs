using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Application.Channels;
using MessagePusher.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MessagePusher.Application.Messaging;

public sealed class AsyncMessageWorker : BackgroundService
{
    private readonly AsyncMessageQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AsyncMessageWorker> _logger;

    public AsyncMessageWorker(AsyncMessageQueue queue, IServiceScopeFactory scopeFactory, ILogger<AsyncMessageWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 回灌与消费必须同时推进：有界队列（128、Wait）下先回灌后消费会在
        // 待恢复消息超过容量时永久等待空位。
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var replay = Task.Run(() => ReplayPendingAsync(lifetime.Token), CancellationToken.None);

        try
        {
            await foreach (var id in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessAsync(id, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "async message sender error");
                }
            }
        }
        finally
        {
            await lifetime.CancelAsync();
            await replay;
        }
    }

    private async Task ReplayPendingAsync(CancellationToken ct)
    {
        try
        {
            await LoadPendingAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 停机取消属于正常路径。
        }
        catch (Exception ex)
        {
            // 运行期间立即观察回灌失败，不能等消费循环退出时才报告。
            _logger.LogError(ex, "async message replay error");
        }
    }

    private async Task LoadPendingAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var messages = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
        var ids = await messages.GetAsyncPendingIdsAsync(ct);
        foreach (var id in ids)
            await _queue.EnqueueAsync(id, ct);
    }

    private async Task ProcessAsync(int id, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var messages = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var channels = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
        var factory = scope.ServiceProvider.GetRequiredService<ChannelProviderFactory>();

        var message = await messages.GetByIdAsync(id, ct);
        if (message is null)
        {
            _logger.LogError("async message sender error: message {Id} not found", id);
            return;
        }

        // 单消费者下，实时入队和恢复快照可能包含同一 ID，跳过已处理项。
        // 不扩展为跨进程严格一次投递保证。
        if (message.Status != (int)MessageSendStatus.AsyncPending)
            return;

        var status = (int)MessageSendStatus.Failed;
        try
        {
            var user = await users.GetByIdAsync(message.UserId, false, ct)
                       ?? throw new InvalidOperationException("user not found");
            var channel = await channels.GetByNameAsync(message.Channel, user.Id, ct)
                          ?? throw new InvalidOperationException("channel not found");
            await factory.Resolve(channel.Type).SendAsync(message, user, channel, ct);
            status = (int)MessageSendStatus.Sent;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "async message sender error");
        }

        await messages.UpdateStatusAsync(message, status, ct);
    }
}
