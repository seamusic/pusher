namespace MessagePusher.Web.Api;

public sealed class StatusInfo
{
    public string Version { get; set; } = "";
    public long StartTime { get; set; }
    public string SystemName { get; set; } = "";
    public string HomePageLink { get; set; } = "";
    public string FooterHtml { get; set; } = "";
    public string ServerAddress { get; set; } = "";
    public int MessageCount { get; set; }
    public int UserCount { get; set; }
}
