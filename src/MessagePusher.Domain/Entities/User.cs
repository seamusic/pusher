using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace MessagePusher.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Role { get; set; } = Roles.Common;
    public int Status { get; set; } = (int)UserStatus.Enabled;
    public string Token { get; set; } = "";
    public string Email { get; set; } = "";
    public string GitHubId { get; set; } = "";
    public string WeChatId { get; set; } = "";
    public string Channel { get; set; } = "";
    public int SendEmailToOthers { get; set; }
    public int SaveMessageToDatabase { get; set; }

    [NotMapped]
    [JsonPropertyName("verification_code")]
    public string? VerificationCode { get; set; }
}
