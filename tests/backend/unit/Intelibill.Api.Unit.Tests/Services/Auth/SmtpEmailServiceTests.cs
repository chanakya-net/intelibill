using System.Globalization;
using System.Text.RegularExpressions;
using Intelibill.Infrastructure.Options;
using Intelibill.Infrastructure.Services.Auth;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace Intelibill.Api.Unit.Tests.Services.Auth;

public class SmtpEmailServiceTests
{
    private static EmailOptions Options(bool useStartTls = true) => new()
    {
        Enabled = true,
        Host = "smtp.office365.com",
        Port = useStartTls ? 587 : 465,
        UseStartTls = useStartTls,
        Username = "noreply@example.com",
        Password = "secret",
        FromEmail = "noreply@example.com",
        FromName = "Intelibill"
    };

    [Fact]
    public async Task SendIdentityAsync_ComposesExpectedMessageAndUsesStartTls()
    {
        var smtpClient = new RecordingSmtpClient();
        var service = new SmtpEmailService(Microsoft.Extensions.Options.Options.Create(Options()), new RecordingSmtpClientFactory(smtpClient));

        await service.SendIdentityAsync("user@test.com", "Reset your Intelibill password", "body text", CancellationToken.None);

        Assert.Equal("smtp.office365.com", smtpClient.Host);
        Assert.Equal(587, smtpClient.Port);
        Assert.Equal(SecureSocketOptions.StartTls, smtpClient.SocketOptions);
        Assert.Equal("noreply@example.com", smtpClient.Username);
        Assert.Equal("secret", smtpClient.Password);
        Assert.True(smtpClient.Disconnected);

        var message = Assert.IsType<MimeMessage>(smtpClient.SentMessage);
        Assert.Equal("Reset your Intelibill password", message.Subject);
        var from = Assert.IsType<MailboxAddress>(Assert.Single(message.From));
        var to = Assert.IsType<MailboxAddress>(Assert.Single(message.To));
        Assert.Equal("Intelibill", from.Name);
        Assert.Equal("noreply@example.com", from.Address);
        Assert.Equal("user@test.com", to.Address);
        Assert.Equal("body text", Assert.IsType<TextPart>(message.Body).Text);
    }

    [Fact]
    public async Task SendIdentityAsync_WithoutStartTls_RequiresImplicitTls()
    {
        var smtpClient = new RecordingSmtpClient();
        var service = new SmtpEmailService(Microsoft.Extensions.Options.Options.Create(Options(useStartTls: false)), new RecordingSmtpClientFactory(smtpClient));

        await service.SendIdentityAsync("user@test.com", "subject", "body", CancellationToken.None);

        Assert.Equal(SecureSocketOptions.SslOnConnect, smtpClient.SocketOptions);
    }

    [Fact]
    public void SocketOptions_NeverAllowOpportunisticPlaintext()
    {
        Assert.NotEqual(SecureSocketOptions.Auto, SmtpEmailService.SocketOptionsFor(Options(useStartTls: true)));
        Assert.NotEqual(SecureSocketOptions.Auto, SmtpEmailService.SocketOptionsFor(Options(useStartTls: false)));
    }

    [Fact]
    public void PasswordResetBody_ContainsLinkOnceAndIstExpiry()
    {
        var resetLink = "https://inventory.test/reset-password?token=abc&email=user%40test.com";
        var expiresAt = new DateTimeOffset(2026, 5, 11, 10, 30, 0, TimeSpan.Zero);

        var body = IdentityMailOutbox.BuildPasswordResetBody(resetLink, expiresAt);

        var expectedExpiry = expiresAt.ToOffset(TimeSpan.FromHours(5.5)).ToString("hh:mm tt", CultureInfo.InvariantCulture);
        Assert.Contains("Here is your password reset link:", body);
        Assert.Single(Regex.Matches(body, Regex.Escape(resetLink)));
        Assert.Contains($"This link expires at {expectedExpiry} IST and can be used only once.", body);
        Assert.Contains("If you did not request this, you can ignore this email.", body);
    }

    private sealed class RecordingSmtpClient : ISmtpClient
    {
        public string? Host { get; private set; }

        public int Port { get; private set; }

        public SecureSocketOptions SocketOptions { get; private set; }

        public string? Username { get; private set; }

        public string? Password { get; private set; }

        public MimeMessage? SentMessage { get; private set; }

        public bool Disconnected { get; private set; }

        public Task ConnectAsync(string host, int port, SecureSocketOptions socketOptions, CancellationToken cancellationToken = default)
        {
            Host = host;
            Port = port;
            SocketOptions = socketOptions;
            return Task.CompletedTask;
        }

        public Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken = default)
        {
            Username = userName;
            Password = password;
            return Task.CompletedTask;
        }

        public Task SendAsync(MimeMessage message, CancellationToken cancellationToken = default)
        {
            SentMessage = message;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default)
        {
            Disconnected = quit;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingSmtpClientFactory(RecordingSmtpClient client) : ISmtpClientFactory
    {
        public ISmtpClient Create() => client;
    }
}
