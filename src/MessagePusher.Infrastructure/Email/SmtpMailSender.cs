using MailKit.Net.Smtp;
using MailKit.Security;
using MessagePusher.Application.Abstractions;
using MessagePusher.Domain;
using MimeKit;

namespace MessagePusher.Infrastructure.Email;

public sealed class SmtpMailSender : IEmailSender
{
    private readonly ISystemOptionService _options;

    public SmtpMailSender(ISystemOptionService options) => _options = options;

    public async Task SendAsync(string subject, string receiver, string htmlContent, CancellationToken ct = default)
    {
        var server = _options.Get("SMTPServer");
        var port = _options.GetInt("SMTPPort", AppDefaults.SmtpPort);
        var account = _options.Get("SMTPAccount");
        var token = _options.Get("SMTPToken");
        var systemName = _options.Get("SystemName", AppDefaults.SystemName);
        var skipVerify = _options.GetBool("SmtpSkipCertificateValidation");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(systemName, account));
        foreach (var addr in receiver.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            message.To.Add(MailboxAddress.Parse(addr));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlContent };

        using var client = new SmtpClient();
        if (skipVerify)
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;

        var secure = port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(server, port, secure, ct);
        if (!string.IsNullOrEmpty(account))
            await client.AuthenticateAsync(account, token, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
