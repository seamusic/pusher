using System.Collections.Concurrent;
using MessagePusher.Domain;
using Microsoft.Extensions.Logging;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Application.Channels;
using MessagePusher.Application.Messaging;
using MessagePusher.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MessagePusher.Application.Tests;

public class AsyncMessageWorkerTests
{
    private sealed class RecordingProvider : IChannelProvider
    {
        private readonly List<int> _sent;
        public RecordingProvider(List<int> sent) => _sent = sent;
        public string Type => "fake";
        public Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
        {
            lock (_sent) _sent.Add(message.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMessageRepository : IMessageRepository
    {
        private readonly IReadOnlyList<int> _pendingIds;
        private readonly bool _failPending;
        public List<(int Id, int Status)> StatusUpdates { get; } = [];
        private readonly ConcurrentDictionary<int, int> _statuses = new();
        private int _reads;
        public int Reads => Volatile.Read(ref _reads);
        public Func<CancellationToken, Task>? BeforeReplay { get; set; }

        public FakeMessageRepository(IReadOnlyList<int> pendingIds, bool failPending = false)
        {
            _pendingIds = pendingIds;
            _failPending = failPending;
        }

        public async Task<IReadOnlyList<int>> GetAsyncPendingIdsAsync(CancellationToken ct = default)
        {
            if (BeforeReplay is not null) await BeforeReplay(ct);
            if (_failPending) throw new InvalidOperationException("pending recovery failed");
            return _pendingIds;
        }

        public Task<Message?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _reads);
            return Task.FromResult<Message?>(new Message
            {
                Id = id, UserId = 1, Channel = "ch",
                Status = _statuses.GetValueOrDefault(id, (int)MessageSendStatus.AsyncPending)
            });
        }

        public Task UpdateStatusAsync(Message message, int status, CancellationToken ct = default)
        {
            _statuses[message.Id] = status;
            lock (StatusUpdates) StatusUpdates.Add((message.Id, status));
            return Task.CompletedTask;
        }

        public Task<Message?> GetByIdsAsync(int id, int userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Message?> GetByLinkAsync(string link, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> GetStatusByLinkAsync(string link, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Message>> GetByUserIdAsync(int userId, int offset, int count, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Message>> SearchAsync(int userId, string keyword, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Message message, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(int id, int userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public Task<User?> GetByIdAsync(int id, bool selectAll, CancellationToken ct = default) =>
            Task.FromResult<User?>(new User { Id = id });

        public Task<IReadOnlyList<User>> GetAllAsync(int offset, int count, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<User>> SearchAsync(string keyword, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<User?> GetByGitHubIdAsync(string githubId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<User?> GetByWeChatIdAsync(string wechatId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> GetMaxIdAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> AnyAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(User user, bool updatePassword, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateFieldsAsync(User user, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteOwnedDataAsync(int userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EmailTakenAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> UsernameTakenAsync(string username, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> GitHubIdTakenAsync(string githubId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> WeChatIdTakenAsync(string wechatId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TokenExistsAsync(string token, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ResetPasswordByEmailAsync(string email, string hashedPassword, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeChannelRepository : IChannelRepository
    {
        public Task<Channel?> GetByNameAsync(string name, int userId, CancellationToken ct = default) =>
            Task.FromResult<Channel?>(new Channel { Name = name, Type = "fake" });

        public Task<Channel?> GetByIdAsync(int id, int userId, bool selectAll, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Channel>> GetByUserIdAsync(int userId, int offset, int count, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BriefChannel>> GetBriefByUserIdAsync(int userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Channel>> SearchAsync(int userId, string keyword, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Channel>> GetTokenStoreChannelsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Channel>> GetTokenStoreChannelsByUserIdAsync(int userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountSharedAsync(string type, string appId, string secret, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Channel channel, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Channel channel, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateStatusAsync(Channel channel, int status, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Channel?> DeleteAsync(int id, int userId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _map;
        public FakeServiceProvider(Dictionary<Type, object> map) => _map = map;
        public object? GetService(Type serviceType) => _map.TryGetValue(serviceType, out var s) ? s : null;
    }

    private sealed class FakeScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _sp;
        public FakeScopeFactory(IServiceProvider sp) => _sp = sp;
        public IServiceScope CreateScope() => new FakeScope(_sp);

        private sealed class FakeScope : IServiceScope
        {
            public FakeScope(IServiceProvider sp) => ServiceProvider = sp;
            public IServiceProvider ServiceProvider { get; }
            public void Dispose() { }
        }
    }

    private static (AsyncMessageWorker Worker, FakeMessageRepository Repo, List<int> Sent, AsyncMessageQueue Queue) CreateWorker(int pendingCount, bool failPending = false, ILogger<AsyncMessageWorker>? logger = null)
    {
        var pending = Enumerable.Range(1, pendingCount).ToList();
        var repo = new FakeMessageRepository(pending, failPending);
        var sent = new List<int>();
        var sp = new FakeServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IMessageRepository)] = repo,
            [typeof(IUserRepository)] = new FakeUserRepository(),
            [typeof(IChannelRepository)] = new FakeChannelRepository(),
            [typeof(ChannelProviderFactory)] = new ChannelProviderFactory([new RecordingProvider(sent)])
        });
        var queue = new AsyncMessageQueue();
        var worker = new AsyncMessageWorker(queue, new FakeScopeFactory(sp), logger ?? NullLogger<AsyncMessageWorker>.Instance);
        return (worker, repo, sent, queue);
    }

    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 10000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return;
            await Task.Delay(20);
        }
        Assert.Fail($"condition not met within {timeoutMs}ms");
    }

    [Fact]
    public async Task Replay_snapshot_overlapping_live_enqueue_does_not_send_twice()
    {
        var (worker, repo, sent, queue) = CreateWorker(1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.BeforeReplay = async ct => { started.SetResult(); await release.Task.WaitAsync(ct); };
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await queue.EnqueueAsync(1, CancellationToken.None);
            await WaitFor(() => { lock (repo.StatusUpdates) return repo.StatusUpdates.Count == 1; });
            release.SetResult(); // 回灌包含已由实时入队路径处理的同一个 ID。
            await WaitFor(() => repo.Reads >= 2);
        }
        finally { release.TrySetResult(); await worker.StopAsync(CancellationToken.None); }
        lock (sent) Assert.Equal(new[] { 1 }, sent);
        lock (repo.StatusUpdates) Assert.Single(repo.StatusUpdates);
    }

    [Fact]
    public async Task Replay_failure_is_logged_before_shutdown()
    {
        var logs = new RecordingLogs();
        using var factory = LoggerFactory.Create(b => b.AddProvider(logs));
        var (worker, _, _, _) = CreateWorker(1, failPending: true, factory.CreateLogger<AsyncMessageWorker>());
        await worker.StartAsync(CancellationToken.None);
        try { await WaitFor(() => logs.Lines.Any(x => x.Contains("replay error")), 1000); }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Startup_recovery_of_more_than_queue_capacity_completes()
    {
        // 复现路径：容量 128 的有界队列，启动时先回灌后消费。
        // 129 条待恢复消息时，旧实现回灌第 129 条等待空位，而消费者尚未启动 → 永久阻塞。
        const int count = 129;
        var (worker, repo, sent, _) = CreateWorker(count);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitFor(() => { lock (sent) return sent.Count == count; });
            await WaitFor(() => { lock (repo.StatusUpdates) return repo.StatusUpdates.Count == count; });
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        lock (sent)
        {
            Assert.Equal(count, sent.Count);
            Assert.Equal(count, sent.Distinct().Count()); // 不因回灌/注册重复发送同一条
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(256)]
    public async Task Startup_recovery_completes_for_boundary_counts(int count)
    {
        var (worker, repo, sent, _) = CreateWorker(count);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            if (count > 0)
            {
                await WaitFor(() => { lock (sent) return sent.Count == count; });
                await WaitFor(() => { lock (repo.StatusUpdates) return repo.StatusUpdates.Count == count; });
            }
            else
            {
                await Task.Delay(200); // 空回灌不应崩溃或挂起
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        lock (sent)
        {
            Assert.Equal(count, sent.Count);
            Assert.Equal(count, sent.Distinct().Count());
        }
    }

    [Fact]
    public async Task Restart_replays_pending_again_no_static_guard()
    {
        var (first, _, sent1, _) = CreateWorker(3);
        await first.StartAsync(CancellationToken.None);
        await WaitFor(() => { lock (sent1) return sent1.Count == 3; });
        await first.StopAsync(CancellationToken.None);

        // 新实例（新队列、同一批待恢复数据）必须重新回灌——静态守卫已移除，无跨实例状态泄漏
        var (second, _, sent2, _) = CreateWorker(3);
        await second.StartAsync(CancellationToken.None);
        try
        {
            await WaitFor(() => { lock (sent2) return sent2.Count == 3; });
        }
        finally
        {
            await second.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Stop_during_large_replay_terminates()
    {
        var (worker, _, _, _) = CreateWorker(5000);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(50); // 让回灌进行中
        var stop = worker.StopAsync(CancellationToken.None);

        var completed = await Task.WhenAny(stop, Task.Delay(10000));
        Assert.Same(stop, completed); // 停机不挂起
    }

    [Fact]
    public async Task Replay_failure_does_not_block_consumption()
    {
        var (worker, _, sent, queue) = CreateWorker(10, failPending: true);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await queue.EnqueueAsync(42, CancellationToken.None);
            await WaitFor(() => { lock (sent) return sent.Contains(42); });
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }
}
