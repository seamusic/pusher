using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace MessagePusher.Domain.Entities;

public class Message
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Content { get; set; } = "";
    public string Url { get; set; } = "";
    public string Channel { get; set; } = "";
    public long Timestamp { get; set; }
    public string Link { get; set; } = "";
    public string To { get; set; } = "";
    public int Status { get; set; }
    public string? RenderMode { get; set; }

    [NotMapped] public string? Token { get; set; }
    [NotMapped]
    [JsonPropertyName("html_content")]
    public string? HtmlContent { get; set; }
    [NotMapped] public bool Async { get; set; }
    [NotMapped] public string? OpenId { get; set; }
    [NotMapped] public string? Desp { get; set; }
    [NotMapped] public string? Short { get; set; }

    // 所有属性均为字符串/值类型，浅拷贝即完全独立副本
    public Message Clone() => (Message)MemberwiseClone();
}
