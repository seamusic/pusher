namespace MessagePusher.Web.Api;

public sealed class UserInfo
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Password { get; set; } = "";
    public int Role { get; set; }
    public int Status { get; set; }
    public string Token { get; set; } = "";
    public string Email { get; set; } = "";
    public string GitHubId { get; set; } = "";
    public string WeChatId { get; set; } = "";
    public string Channel { get; set; } = "";
    public int SendEmailToOthers { get; set; }
    public int SaveMessageToDatabase { get; set; }
    public bool IsAdmin => Role >= 10;
    public bool IsRoot => Role >= 100;
}
