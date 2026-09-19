namespace MessagePusher.Domain.Entities;

public class Webhook
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public int Status { get; set; } = (int)WebhookStatus.Enabled;
    public string Link { get; set; } = "";
    public long CreatedTime { get; set; }
    public string ExtractRule { get; set; } = "";
    public string ConstructRule { get; set; } = "";
    public string Channel { get; set; } = "";
}

public class WebhookConstructRule
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Content { get; set; } = "";
    public string Url { get; set; } = "";
}
