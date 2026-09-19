using Microsoft.AspNetCore.Components.Web;
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
            Nav.NavigateTo("/");
        }
        finally
        {
            _busy = false;
        }
    }
}
