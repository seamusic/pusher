using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MessagePusher.Web.Components.Pages;

public partial class Login
{
    private MudForm? _form;
    private string _username = "";
    private string _password = "";
    private string? _error;
    private bool _busy;
    private bool _showPassword;

    /// <summary>被拦截前想访问的页面，登录后跳回该页。</summary>
    [SupplyParameterFromQuery(Name = "ReturnUrl")]
    public string? ReturnUrl { get; set; }

    /// <summary>仅允许站内相对地址，避免开放重定向。</summary>
    private string RedirectTarget =>
        !string.IsNullOrWhiteSpace(ReturnUrl)
        && ReturnUrl.StartsWith('/')
        && !ReturnUrl.StartsWith("//")
            ? ReturnUrl
            : "/";

    private InputType _passwordInput => _showPassword ? InputType.Text : InputType.Password;
    private string _passwordIcon => _showPassword ? Icons.Material.Filled.VisibilityOff : Icons.Material.Filled.Visibility;

    private void TogglePassword() => _showPassword = !_showPassword;

    private async Task OnPasswordKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            await SubmitAsync();
    }

    private async Task SubmitAsync()
    {
        if (_busy)
            return;
        if (_form is not null)
            await _form.ValidateAsync();
        if (_form is { IsValid: false })
            return;
        _busy = true;
        _error = null;
        try
        {
            var result = await Api.LoginAsync(_username, _password);
            if (!result.Success || result.Data is null)
            {
                _error = string.IsNullOrWhiteSpace(result.Message) ? "用户名或密码不正确" : result.Message;
                return;
            }
            Auth.SignIn(result.Data);
            Nav.NavigateTo(RedirectTarget);
        }
        finally
        {
            _busy = false;
        }
    }
}
