using System.Net;
using MessagePusher.Web.Api;
using MessagePusher.Web.Auth;
using MessagePusher.Web.Components;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddAuthorizationCore();
// Blazor 组件级授权由 AuthorizeRouteView 处理（走 RedirectToLogin）。
// 但页面上的 [Authorize] 同时会成为端点的授权元数据，匿名请求会被 ASP.NET Core
// 授权中间件 Challenge；若不注册认证服务，这里会直接抛 500。
// 注册 Cookie 方案仅为让 Challenge 能重定向到登录页，浏览器的登录态仍由 API 会话维护。
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Cookie.Name = "MessagePusher.Web.Session";
    });
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ApiAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<ApiAuthStateProvider>());
builder.Services.AddScoped(sp =>
{
    var baseUrl = sp.GetRequiredService<IConfiguration>()["Api:BaseUrl"] ?? "http://localhost:3000/";
    if (!baseUrl.EndsWith('/'))
        baseUrl += "/";
    return new HttpClient(new HttpClientHandler
    {
        UseCookies = true,
        CookieContainer = new CookieContainer()
    })
    {
        BaseAddress = new Uri(baseUrl)
    };
});
builder.Services.AddScoped<ApiClient>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.Run();
