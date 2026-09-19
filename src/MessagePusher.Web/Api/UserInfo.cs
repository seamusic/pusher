namespace MessagePusher.Web.Api;

public sealed class UserInfo
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Role { get; set; }
    public int Status { get; set; }
    public bool IsAdmin => Role >= 10;
}
