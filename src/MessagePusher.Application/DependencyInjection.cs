using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Channels;
using MessagePusher.Application.Messaging;
using MessagePusher.Application.Policies;
using MessagePusher.Application.Realtime;
using MessagePusher.Application.Services;
using MessagePusher.Application.Tokens;
using Microsoft.Extensions.DependencyInjection;

namespace MessagePusher.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<UserPolicy>();
        services.AddSingleton<VerificationService>();
        services.AddSingleton<AsyncMessageQueue>();
        services.AddSingleton<SseBroker>();
        services.AddSingleton<ISseBroker>(sp => sp.GetRequiredService<SseBroker>());
        services.AddSingleton<WebSocketClientManager>();
        services.AddSingleton<IWebSocketClientManager>(sp => sp.GetRequiredService<WebSocketClientManager>());
        services.AddSingleton<TokenStore>();
        services.AddSingleton<ITokenStore>(sp => sp.GetRequiredService<TokenStore>());
        services.AddHostedService(sp => sp.GetRequiredService<TokenStore>());
        services.AddHostedService<AsyncMessageWorker>();

        services.AddScoped<PushService>();
        services.AddScoped<UserService>();
        services.AddScoped<AuthService>();
        services.AddScoped<ChannelService>();
        services.AddScoped<MessageService>();
        services.AddScoped<WebhookService>();
        services.AddScoped<OptionService>();
        services.AddScoped<MiscService>();

        services.AddSingleton<IChannelProvider, EmailProvider>();
        services.AddSingleton<IChannelProvider, WeChatTestProvider>();
        services.AddSingleton<IChannelProvider, WeChatCorpProvider>();
        services.AddSingleton<IChannelProvider, CorpProvider>();
        services.AddSingleton<IChannelProvider, LarkProvider>();
        services.AddSingleton<IChannelProvider, LarkAppProvider>();
        services.AddSingleton<IChannelProvider, DingProvider>();
        services.AddSingleton<IChannelProvider, BarkProvider>();
        services.AddSingleton<IChannelProvider, ClientProvider>();
        services.AddSingleton<IChannelProvider, TelegramProvider>();
        services.AddSingleton<IChannelProvider, DiscordProvider>();
        services.AddSingleton<IChannelProvider, OneBotProvider>();
        services.AddSingleton<IChannelProvider, GroupProvider>();
        services.AddSingleton<IChannelProvider, CustomProvider>();
        services.AddSingleton<IChannelProvider, TencentAlarmProvider>();
        services.AddSingleton<IChannelProvider, ServerChanProvider>();
        services.AddSingleton<IChannelProvider, PushDeerProvider>();
        services.AddSingleton<IChannelProvider, PushPlusProvider>();
        services.AddSingleton<IChannelProvider, NoneProvider>();
        services.AddSingleton<ChannelProviderFactory>();
        services.AddSingleton<IChannelConfigValidator, ServerChanConfigValidator>();
        services.AddSingleton<IChannelConfigValidator, PushDeerConfigValidator>();
        services.AddSingleton<IChannelConfigValidator, PushPlusConfigValidator>();
        services.AddSingleton<ChannelConfigValidatorRegistry>();

        services.AddHttpClient("channels", c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient("token-store", c => c.Timeout = TimeSpan.FromSeconds(5));
        services.AddHttpClient("oauth", c => c.Timeout = TimeSpan.FromSeconds(5));
        return services;
    }
}
