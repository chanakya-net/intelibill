using MailKit.Net.Smtp;
using Microsoft.Extensions.Hosting;

namespace Intelibill.Infrastructure.Services.Auth;

internal sealed class MailKitSmtpClientFactory(IHostEnvironment environment) : ISmtpClientFactory
{
    /// <summary>
    /// MailKit verifies certificate revocation by default. macOS cannot complete that OCSP/CRL
    /// lookup, so the TLS handshake with a real SMTP provider is rejected there and a local run
    /// cannot reach one at all. Development and Testing therefore skip the check; every other
    /// environment, Production included, keeps it.
    /// </summary>
    internal bool ChecksCertificateRevocation { get; } =
        !environment.IsDevelopment() && !environment.IsEnvironment("Testing");

    public ISmtpClient Create() => new MailKitSmtpClientAdapter(ChecksCertificateRevocation);

    internal sealed class MailKitSmtpClientAdapter(bool checkCertificateRevocation) : ISmtpClient
    {
        private readonly SmtpClient _client = new() { CheckCertificateRevocation = checkCertificateRevocation };

        internal bool ChecksCertificateRevocation => _client.CheckCertificateRevocation;

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
