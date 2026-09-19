namespace MessagePusher.Domain.Entities;

public class Channel
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Status { get; set; } = (int)ChannelStatus.Enabled;
    public string Secret { get; set; } = "";
    public string AppId { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string Url { get; set; } = "";
    public string Other { get; set; } = "";
    public long CreatedTime { get; set; }
    public string? Token { get; set; }
}

public class BriefChannel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}
