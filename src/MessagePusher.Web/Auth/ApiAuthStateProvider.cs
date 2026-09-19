using System.Security.Claims;
using MessagePusher.Web.Api;
using Microsoft.AspNetCore.Components.Authorization;

namespace MessagePusher.Web.Auth;

public sealed class ApiAuthStateProvider : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());
    private ClaimsPrincipal _current = Anonymous;

    public UserInfo? User { get; private set; }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(_current));

    public void SignIn(UserInfo user)
    {
        User = user;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.IsAdmin ? "Admin" : "User")
        };
        if (user.Role >= 100)
            claims.Add(new(ClaimTypes.Role, "Root"));
        _current = new ClaimsPrincipal(new ClaimsIdentity(claims, "ApiCookie"));
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public void SignOut()
    {
        User = null;
        _current = Anonymous;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
