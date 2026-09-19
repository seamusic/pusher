using MessagePusher.Application.Abstractions;

namespace MessagePusher.Application.Services;

public sealed class MiscService
{
    private readonly ISystemOptionService _options;
    private readonly IAppCounters _counters;

    public MiscService(ISystemOptionService options, IAppCounters counters)
    {
        _options = options;
        _counters = counters;
    }

    public object GetStatus(string version) => new
    {
        version,
        start_time = _counters.StartTimeUnix,
        email_verification = _options.GetBool("EmailVerificationEnabled"),
        github_oauth = _options.GetBool("GitHubOAuthEnabled"),
        github_client_id = _options.Get("GitHubClientId"),
        system_name = _options.Get("SystemName", Domain.AppDefaults.SystemName),
        home_page_link = _options.Get("HomePageLink"),
        footer_html = _options.Get("Footer"),
        wechat_qrcode = _options.Get("WeChatAccountQRCodeImageURL"),
        wechat_login = _options.GetBool("WeChatAuthEnabled"),
        server_address = _options.Get("ServerAddress", Domain.AppDefaults.ServerAddress),
        turnstile_check = _options.GetBool("TurnstileCheckEnabled"),
        turnstile_site_key = _options.Get("TurnstileSiteKey"),
        message_persistence = _options.GetBool("MessagePersistenceEnabled", true),
        message_render = _options.GetBool("MessageRenderEnabled", true),
        message_count = _counters.MessageCount,
        user_count = _counters.UserCount
    };

    public string GetNotice() => _options.Get("Notice");
    public string GetAbout() => _options.Get("About");
}
