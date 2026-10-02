using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace FinansApi.Infrastructure.Email;

public sealed class SmtpOptions
{
    public string? Host
    {
        get; set;
    }
    public int Port { get; set; } = 1025;
    public string? From
    {
        get; set;
    }
    public string? Username
    {
        get; set;
    }
    public string? Password
    {
        get; set;
    }
    public bool EnableSsl
    {
        get; set;
    }
}

public record EmailPayload(string? Email, string Subject, string Body, int? InvoiceId = null);

public interface IEmailSender
{
    Task Send(EmailPayload payload, CancellationToken cancellationToken);

}

public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task Send(EmailPayload payload, CancellationToken cancellationToken)
    {
        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.Host) || string.IsNullOrWhiteSpace(config.From)
            || string.IsNullOrWhiteSpace(payload.Email))
        {
            throw new InvalidOperationException("SMTP veya alıcı e-posta ayarı eksik.");
        }

        using var client = new SmtpClient(config.Host, config.Port)
        {
            EnableSsl = config.EnableSsl
        };
        if (!string.IsNullOrWhiteSpace(config.Username))
        {
            client.Credentials = new NetworkCredential(config.Username, config.Password);
        }

        using var mail = new MailMessage(config.From, payload.Email, payload.Subject, payload.Body)
        {
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8

        };
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString($"<html><body><p>{WebUtility.HtmlEncode(payload.Body)}</p></body></html>", System.Text.Encoding.UTF8, "text/html"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await client.SendMailAsync(mail, timeout.Token);
    }

}
