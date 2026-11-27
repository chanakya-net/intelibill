using MailKit.Security;
using MimeKit;

namespace Intelibill.Infrastructure.Services.Auth;

internal interface ISmtpClient : IAsyncDisposable
{
    Task ConnectAsync(string host, int port, SecureSocketOptions socketOptions, CancellationToken cancellationToken = default);

    Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken = default);

    Task SendAsync(MimeMessage message, CancellationToken cancellationToken = default);

    Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default);
}
