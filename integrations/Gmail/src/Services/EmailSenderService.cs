using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Integrations.Gmail.Services;

internal sealed class EmailSenderService(
    IOptions<SmtpOptions> smtpOptions,
    ILogger<EmailSenderService> logger) : IAsyncDisposable
{
    private readonly SmtpOptions _options = smtpOptions.Value;
    private readonly SmtpClient _client = new();
    private readonly SemaphoreSlim _sendLock = new(1);
    private bool _disposed;

    public async Task SendAsync(
        IEnumerable<MailboxAddress> recipients,
        string subject,
        string body,
        bool isHtml,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(EmailSenderService));

        var message = CreateMessage(recipients, subject, body, isHtml);
        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            await EnsureConnectedAsync(cancellationToken);
            await EnsureAuthenticatedAsync(cancellationToken);
            await _client.SendAsync(message, cancellationToken);
            logger.LogInformation("Email sent to {RecipientCount} recipients.", message.To.Count);
        }
        catch
        {
            await SafeDisconnectAsync();
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private MimeMessage CreateMessage(
        IEnumerable<MailboxAddress> recipients,
        string subject,
        string body,
        bool isHtml)
    {
        var message = new MimeMessage();
        var mailContentType = isHtml ? "html" : "plain";

        message.From.Add(new MailboxAddress(_options.FromName ?? string.Empty, _options.From));
        message.To.AddRange(recipients);
        message.Subject = subject;
        message.Body = new TextPart(mailContentType) { Text = body };

        return message;
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected) return;

        var socketOption = _options.UseSsl
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTlsWhenAvailable;

        await _client.ConnectAsync(
            _options.Host,
            _options.Port,
            socketOption,
            cancellationToken);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (_client.IsAuthenticated) return;

        var user = _options.User;
        var password = _options.Password;

        if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(password))
            await _client.AuthenticateAsync(user, password, cancellationToken);
    }

    private async Task SafeDisconnectAsync()
    {
        if (_client.IsConnected) return;

        try
        {
            await _client.DisconnectAsync(true, CancellationToken.None);
        }
        catch
        {
            // ignored
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;
        await _sendLock.WaitAsync();

        try
        {
            await SafeDisconnectAsync();
            _client.Dispose();
        }
        finally
        {
            _sendLock.Release();
            _sendLock.Dispose();
        }
    }
}
