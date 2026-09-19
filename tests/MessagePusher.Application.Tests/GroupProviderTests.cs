using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Abstractions.Repositories;
using MessagePusher.Application.Channels;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace MessagePusher.Application.Tests;

public class GroupProviderTests
{
    private sealed record SentPayload(string ChannelName, string To, string Title, string Description, string Content, string Url);

    private sealed class RecordingProvider : IChannelProvider
    {
        private readonly List<SentPayload> _sent;
        private readonly bool _mutating;

        public RecordingProvider(string type, List<SentPayload> sent, bool mutating = false)
        {
            Type = type;
            _sent = sent;
            _mutating = mutating;
        }

        public string Type { get; }

        public Task SendAsync(Message message, User user, Channel channel, CancellationToken ct)
        {
            _sent.Add(new SentPayload(channel.Name, message.To, message.Title, message.Description, message.Content, message.Url));
            if (_mutating)
            {
                message.To = "mutated-to";
                message.Title = "mutated-title";
                message.Description = "mutated-desc";
                message.Content = "mutated-content";
                message.Url = "mutated-url";
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeChannelRepository : IChannelRepository
    {
        private readonly Dictionary<string, Channel> _byName;

        public FakeChannelRepository(params Channel[] channels) =>
            _byName = channels.ToDictionary(c => c.Name, StringComparer.Ordinal);

        public Task<Channel?> GetByNameAsync(string name, int userId, CancellationToken ct = default) =>
            Task.FromResult(_byName.TryGetValue(name, out var c) ? c : null);

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

    private sealed class FakeScope : IServiceScope
    {
        public FakeScope(IServiceProvider serviceProvider) => ServiceProvider = serviceProvider;
        public IServiceProvider ServiceProvider { get; }
        public void Dispose() { }
    }

    private sealed class FakeScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public FakeScopeFactory(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

        public IServiceScope CreateScope() => new FakeScope(_serviceProvider);
    }

    private static (GroupProvider Provider, List<SentPayload> Sent) Create(
        Channel[] subChannels, Func<string, List<SentPayload>, IChannelProvider> providerFactory)
    {
        var sent = new List<SentPayload>();
        var providers = subChannels.Select(c => providerFactory(c.Type, sent)).ToList();
        var sp = new FakeServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IChannelRepository)] = new FakeChannelRepository(subChannels),
            [typeof(ChannelProviderFactory)] = new ChannelProviderFactory(providers)
        });
        return (new GroupProvider(new FakeScopeFactory(sp)), sent);
    }

    private static Channel Sub(string name) => new() { Name = name, Type = name };

    private static Message NewMessage() => new()
    {
        Title = "T",
        Description = "D",
        Content = "C",
        Url = "U",
        To = "",
        Channel = "grp"
    };

    [Fact]
    public async Task Mutating_subchannel_does_not_affect_later_subchannels_or_original()
    {
        var group = new Channel { Name = "grp", Type = ChannelType.Group, AppId = "chA|chB", AccountId = "tA|tB" };
        var (provider, sent) = Create(
            [Sub("chA"), Sub("chB")],
            (type, list) => new RecordingProvider(type, list, mutating: type == "chA"));

        var message = NewMessage();
        await provider.SendAsync(message, new User { Id = 1 }, group, CancellationToken.None);

        Assert.Equal(
            [new SentPayload("chA", "tA", "T", "D", "C", "U"), new SentPayload("chB", "tB", "T", "D", "C", "U")],
            sent);
        Assert.Equal("", message.To);
        Assert.Equal("grp", message.Channel);
        Assert.Equal("T", message.Title);
        Assert.Equal("D", message.Description);
        Assert.Equal("C", message.Content);
        Assert.Equal("U", message.Url);
    }

    [Fact]
    public async Task Swapping_subchannel_order_yields_same_per_channel_payloads()
    {
        var group = new Channel { Name = "grp", Type = ChannelType.Group, AppId = "chB|chA", AccountId = "tB|tA" };
        var (provider, sent) = Create(
            [Sub("chA"), Sub("chB")],
            (type, list) => new RecordingProvider(type, list, mutating: type == "chA"));

        await provider.SendAsync(NewMessage(), new User { Id = 1 }, group, CancellationToken.None);

        Assert.Equal(
            [new SentPayload("chB", "tB", "T", "D", "C", "U"), new SentPayload("chA", "tA", "T", "D", "C", "U")],
            sent);
    }

    [Fact]
    public async Task Message_To_overrides_configured_subtargets()
    {
        var group = new Channel { Name = "grp", Type = ChannelType.Group, AppId = "chA|chB", AccountId = "tA|tB" };
        var (provider, sent) = Create(
            [Sub("chA"), Sub("chB")],
            (type, list) => new RecordingProvider(type, list));

        var message = NewMessage();
        message.To = "xA|xB";
        await provider.SendAsync(message, new User { Id = 1 }, group, CancellationToken.None);

        Assert.Equal(["xA", "xB"], sent.Select(s => s.To));
    }

    [Fact]
    public async Task Fixed_target_subchannel_receives_empty_to_when_mixed()
    {
        var group = new Channel { Name = "grp", Type = ChannelType.Group, AppId = "chA|chB", AccountId = "tA|" };
        var (provider, sent) = Create(
            [Sub("chA"), Sub("chB")],
            (type, list) => new RecordingProvider(type, list));

        await provider.SendAsync(NewMessage(), new User { Id = 1 }, group, CancellationToken.None);

        Assert.Equal("tA", sent[0].To);
        Assert.Equal("", sent[1].To);
    }

    [Fact]
    public async Task Nested_group_subchannel_is_rejected()
    {
        var group = new Channel { Name = "grp", Type = ChannelType.Group, AppId = "chA", AccountId = "tA" };
        var nested = new Channel { Name = "chA", Type = ChannelType.Group };
        var (provider, _) = Create([nested], (type, list) => new RecordingProvider(type, list));

        await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(NewMessage(), new User { Id = 1 }, group, CancellationToken.None));
    }

    [Fact]
    public async Task Subchannel_count_mismatch_is_rejected()
    {
        var group = new Channel { Name = "grp", Type = ChannelType.Group, AppId = "chA|chB", AccountId = "tA" };
        var (provider, _) = Create([Sub("chA"), Sub("chB")], (type, list) => new RecordingProvider(type, list));

        await Assert.ThrowsAsync<BusinessException>(() =>
            provider.SendAsync(NewMessage(), new User { Id = 1 }, group, CancellationToken.None));
    }
}
