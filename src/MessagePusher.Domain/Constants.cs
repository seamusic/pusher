namespace MessagePusher.Domain;

public static class Roles
{
    public const int Guest = 0;
    public const int Common = 1;
    public const int Admin = 10;
    public const int Root = 100;
}

public static class ChannelType
{
    public const string Email = "email";
    public const string WeChatTestAccount = "test";
    public const string WeChatCorpAccount = "corp_app";
    public const string Corp = "corp";
    public const string Lark = "lark";
    public const string LarkApp = "lark_app";
    public const string Ding = "ding";
    public const string Bark = "bark";
    public const string Client = "client";
    public const string Telegram = "telegram";
    public const string Discord = "discord";
    public const string OneBot = "one_bot";
    public const string Group = "group";
    public const string Custom = "custom";
    public const string TencentAlarm = "tencent_alarm";
    public const string ServerChan = "server_chan";
    public const string PushDeer = "pushdeer";
    public const string PushPlus = "push_plus";
    public const string Ntfy = "ntfy";
    public const string Gotify = "gotify";
    public const string Pushover = "pushover";
    public const string WxPusher = "wx_pusher";
    public const string PushMe = "pushme";
    public const string None = "none";
}

public static class UserPreference
{
    public const int Unset = 0;
    public const int Allowed = 1;
    public const int Disallowed = 2;
}

public static class AppDefaults
{
    public const string SystemName = "消息推送服务";
    public const string ServerAddress = "http://localhost:3000";
    public const int SmtpPort = 587;
    public const string SqlitePath = "message-pusher.db";
    public const int ItemsPerPage = 10;
    public const int VerificationValidMinutes = 10;
    public const int TokenStoreExpirationSeconds = 2 * 55 * 60;
    public const int VerificationMapMaxSize = 10;
    public const string RootUsername = "root";
    public const string RootPassword = "123456";
    public const string RootDisplayName = "Root User";
}

public static class RateLimitDefaults
{
    public const int GlobalApiNum = 60;
    public const int GlobalApiDurationSeconds = 180;
    public const int GlobalWebNum = 60;
    public const int GlobalWebDurationSeconds = 180;
    public const int CriticalNum = 20;
    public const int CriticalDurationSeconds = 1200;
    public const int UploadNum = 10;
    public const int UploadDurationSeconds = 60;
    public const int DownloadNum = 10;
    public const int DownloadDurationSeconds = 60;
    public const int KeyExpirationMinutes = 20;

    public const string MarkApi = "GA";
    public const string MarkWeb = "GW";
    public const string MarkCritical = "CT";
    public const string MarkUpload = "UP";
    public const string MarkDownload = "DW";
}

public static class VerificationPurpose
{
    public const string Email = "v";
    public const string PasswordReset = "r";
}

public static class AuthClaims
{
    public const string Id = "id";
    public const string Username = "username";
    public const string Role = "role";
    public const string Status = "status";
    public const string Turnstile = "turnstile";
}
