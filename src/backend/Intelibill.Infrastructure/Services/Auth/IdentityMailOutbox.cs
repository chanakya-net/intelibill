using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
namespace Intelibill.Infrastructure.Services.Auth;

internal sealed class IdentityMailOutbox(IEmailOwnershipRepository repository, IConfiguration configuration,
    IOptions<EmailOptions> emailOptions) : IIdentityMailOutbox
{
    public bool Enabled => emailOptions.Value.Enabled && TryKey(out _);
    public void QueueChallenge(Guid id, string destination, string purpose, string token, DateTimeOffset expiresAt)
    {
        var baseUrl = configuration["App:BaseUrl"]?.TrimEnd('/') ?? throw new InvalidOperationException("App:BaseUrl is required.");
        var link = $"{baseUrl}/auth/verify-email#challengeId={id}&token={Uri.EscapeDataString(token)}&purpose={purpose}";
        var subject = purpose == "email-change" ? "Confirm your new Intelibill email" : purpose == "claim" ? "Recover your Intelibill account" : "Verify your Intelibill email";
        var body = $"{subject}\n\n{link}\n\nThis single-use link expires at {expiresAt:O}. Choose your own password when confirming enrollment or recovery.\nIf you did not request this, ignore this email.";
        Queue(id, destination, subject, body, expiresAt);
    }
    public void QueueShopInvitation(Guid invitationId, string destination, string shopName, string role, string token, DateTimeOffset expiresAt)
    {
        var baseUrl = configuration["App:BaseUrl"]?.TrimEnd('/') ?? throw new InvalidOperationException("App:BaseUrl is required.");
        var link = $"{baseUrl}/invitations/accept#invitationId={invitationId}&token={Uri.EscapeDataString(token)}&purpose=shop-invitation";
        var subject = $"You are invited to {shopName} on Intelibill";
        var body = $"{subject}\n\n{link}\n\nYou have been invited as {role}. Sign in to the Intelibill account that uses this email address, then open the single-use link above; it expires at {expiresAt:O}.\nAccepting adds this shop to your account. It does not give the shop control of your sign-in details.\nIf you did not expect this, ignore this email.";
        QueueInvitation(invitationId, destination, subject, body, expiresAt);
    }

    public void QueueSecurityNotice(string destination) => Queue(null, destination, "Your Intelibill email changed",
        "Your Intelibill sign-in email was changed. Existing sessions have been signed out. If this was not you, contact support immediately.", DateTimeOffset.UtcNow.AddDays(1));
    private void QueueInvitation(Guid invitationId, string destination, string subject, string body, DateTimeOffset expiresAt) =>
        repository.AddMail(IdentityMail.CreateForInvitation(invitationId, destination, Encrypt(destination, subject, body), expiresAt));
    private void Queue(Guid? id, string destination, string subject, string body, DateTimeOffset expiresAt) =>
        repository.AddMail(IdentityMail.Create(id, destination, Encrypt(destination, subject, body), expiresAt));
    private string Encrypt(string destination, string subject, string body)
    {
        if (!TryKey(out var key)) throw new InvalidOperationException("Identity:OutboxKey must contain a base64-encoded 32-byte key.");
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new IdentityMailContent(subject, body));
        var nonce = RandomNumberGenerator.GetBytes(12); var cipher = new byte[plaintext.Length]; var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, cipher, tag, Encoding.UTF8.GetBytes(destination));
        CryptographicOperations.ZeroMemory(plaintext);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }
    internal IdentityMailContent Decrypt(IdentityMail mail)
    {
        if (!TryKey(out var key)) throw new InvalidOperationException("Identity mail encryption key is unavailable.");
        var data = Convert.FromBase64String(mail.EncryptedContent); var plain = new byte[data.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), plain, Encoding.UTF8.GetBytes(mail.Destination));
        try { return JsonSerializer.Deserialize<IdentityMailContent>(plain) ?? throw new InvalidOperationException("Invalid identity mail payload."); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private bool TryKey(out byte[] key)
    {
        try { key = Convert.FromBase64String(configuration["Identity:OutboxKey"] ?? string.Empty); return key.Length == 32; }
        catch (FormatException) { key = []; return false; }
    }
}
internal sealed record IdentityMailContent(string Subject, string Body);
