using MudBlazor;

namespace MessagePusher.Web.Api;

public sealed class MessageDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Content { get; set; } = "";
    public string Url { get; set; } = "";
    public string Channel { get; set; } = "";
    public long Timestamp { get; set; }
    public string Link { get; set; } = "";
    public string To { get; set; } = "";
    public int Status { get; set; }
    public string? HtmlContent { get; set; }
}

public sealed class ChannelDto
{
    public int Id { get; set; }
    public string Type { get; set; } = "none";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Status { get; set; } = 1;
    public string Secret { get; set; } = "";
    public string AppId { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string Url { get; set; } = "";
    public string Other { get; set; } = "";
    public long CreatedTime { get; set; }
    public string? Token { get; set; }
}

public sealed class BriefChannelDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class WebhookDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Status { get; set; } = 1;
    public string Link { get; set; } = "";
    public long CreatedTime { get; set; }
    public string ExtractRule { get; set; } = "";
    public string ConstructRule { get; set; } = "";
    public string Channel { get; set; } = "";
}

public sealed class OptionDto
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class PushResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string Uuid { get; set; } = "";
}

public sealed class PushPayload
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Content { get; set; } = "";
    public string Url { get; set; } = "";
    public string Channel { get; set; } = "";
    public string To { get; set; } = "";
    public bool Async { get; set; }
    public string? Token { get; set; }
}

public static class ChannelTypes
{
    public static readonly (string Value, string Text)[] All =
    [
        ("email", "邮件"),
        ("test", "微信测试号"),
        ("corp_app", "企业微信应用号"),
        ("corp", "企业微信群机器人"),
        ("lark", "飞书群机器人"),
        ("lark_app", "飞书自建应用"),
        ("ding", "钉钉群机器人"),
        ("bark", "Bark App"),
        ("client", "WebSocket 客户端"),
        ("telegram", "Telegram 机器人"),
        ("discord", "Discord 群机器人"),
        ("one_bot", "OneBot 协议"),
        ("custom", "自定义消息通道"),
        ("group", "群组消息"),
        ("tencent_alarm", "腾讯云消息告警"),
        ("none", "不推送")
    ];

    public static string Name(string? type) =>
        All.FirstOrDefault(x => x.Value == type).Text ?? type ?? "-";
}

public static class Labels
{
    public static string MessageStatus(int status) => status switch
    {
        1 => "正在发送",
        2 => "发送成功",
        3 => "发送失败",
        4 => "已在队列",
        _ => "未知状态"
    };

    public static Color MessageStatusColor(int status) => status switch
    {
        1 => Color.Info,
        2 => Color.Success,
        3 => Color.Error,
        4 => Color.Warning,
        _ => Color.Default
    };

    public static string Enabled(int status) => status == 1 ? "已启用" : status == 2 ? "已禁用" : "未知";
    public static Color EnabledColor(int status) => status == 1 ? Color.Success : status == 2 ? Color.Error : Color.Default;

    public static string Role(int role) => role switch
    {
        1 => "普通用户",
        10 => "管理员",
        100 => "超级管理员",
        _ => "未知身份"
    };

    public static Color RoleColor(int role) => role switch
    {
        10 => Color.Warning,
        100 => Color.Error,
        _ => Color.Default
    };

    public static string Time(long unix) => unix <= 0
        ? "-"
        : DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
}
