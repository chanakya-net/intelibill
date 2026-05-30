using MailKit.Net.Smtp;

namespace Intelibill.Infrastructure.Services.Auth;

internal sealed class MailKitSmtpClientFactory : ISmtpClientFactory
{
    public ISmtpClient Create() => new MailKitSmtpClientAdapter();

    private sealed class MailKitSmtpClientAdapter : ISmtpClient
    {
        private readonly SmtpClient _client = new();

        public Task ConnectAsync(string host, int port, MailKit.Security.SecureSocketOptions socketOptions, CancellationToken cancellationToken = default) =>
            _client.ConnectAsync(host, port, socketOptions, cancellationToken);

        public Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken = default) =>
            _client.AuthenticateAsync(userName, password, cancellationToken);

        public Task SendAsync(MimeKit.MimeMessage message, CancellationToken cancellationToken = default) =>
            _client.SendAsync(message, cancellationToken);

        public Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default) =>
            _client.DisconnectAsync(quit, cancellationToken);

        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
