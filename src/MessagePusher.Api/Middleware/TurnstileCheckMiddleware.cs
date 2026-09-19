using System.Text.Json.Serialization;
using MessagePusher.Application.Abstractions;
using MessagePusher.Application.Results;
using MessagePusher.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

namespace MessagePusher.Api.Middleware;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class TurnstileCheckAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<ISystemOptionService>();
        if (!options.GetBool("TurnstileCheckEnabled"))
        {
            await next();
            return;
        }

        var http = context.HttpContext;
        if (http.User.HasClaim(AuthClaims.Turnstile, "true"))
        {
            await next();
            return;
        }

        var token = http.Request.Query["turnstile"].ToString();
        if (string.IsNullOrEmpty(token))
        {
            http.Response.StatusCode = StatusCodes.Status200OK;
            await http.Response.WriteAsJsonAsync(ApiResult.Fail("Turnstile token 为空"));
            return;
        }

        var factory = http.RequestServices.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("oauth");
        using var res = await client.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = options.Get("TurnstileSecretKey"),
                ["response"] = token,
                ["remoteip"] = http.Connection.RemoteIpAddress?.ToString() ?? ""
            }));
        var body = await res.Content.ReadFromJsonAsync<TurnstileRes>();
        if (body is null || !body.Success)
        {
            http.Response.StatusCode = StatusCodes.Status200OK;
            await http.Response.WriteAsJsonAsync(ApiResult.Fail("Turnstile 校验失败，请刷新重试！"));
            return;
        }

        if (http.User.Identity?.IsAuthenticated == true)
        {
            var claims = http.User.Claims.ToList();
            claims.RemoveAll(c => c.Type == AuthClaims.Turnstile);
            claims.Add(new Claim(AuthClaims.Turnstile, "true"));
            var id = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id));
        }
        else
        {
            http.Items[AuthClaims.Turnstile] = true;
        }

        await next();
    }

    private sealed class TurnstileRes
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
    }
}
