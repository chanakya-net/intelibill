using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Services.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Recovery mail travels through the same encrypted outbox as every other identity message: the
/// reset proof and its only link commit together, a provider failure retries the same link, and a
/// superseded or used proof is never sent.
/// </summary>
[Collection("Integration Tests")]
public sealed class SecurityMailDeliveryTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string Password = "OwnersPassword!1";

    private readonly ApiWebApplicationFactory _factory = new(fixture, services =>
    {
        services.RemoveAll<IEmailService>();
        services.AddSingleton<FlakyMailTransport>();
        services.AddSingleton<IEmailService>(sp => sp.GetRequiredService<FlakyMailTransport>());
    });

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() { _factory.Dispose(); return Task.CompletedTask; }
    public void Dispose() { _factory.Dispose(); GC.SuppressFinalize(this); }

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
    private static string Email() => $"owner-{Guid.NewGuid():N}@test.com";
    private FlakyMailTransport Transport => _factory.Services.GetRequiredService<FlakyMailTransport>();
    private IdentityMailWorker Worker() => new(_factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<IdentityMailWorker>.Instance);

    private async Task<JsonElement> EnrollAsync(HttpClient client, string email)
    {
        var result = await _factory.EnrollAsync(client, new { email, password = Password, firstName = "Mailbox", lastName = "Owner" });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        // Enrollment mail is not what these tests are about; take it out of the worker's way.
        await SqlAsync("UPDATE identity_mail SET completed_at = CURRENT_TIMESTAMP, encrypted_content = '' WHERE completed_at IS NULL");
        return await result.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task SqlAsync(string sql)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlRawAsync(sql);
    }

    private async Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    [Fact]
    public async Task ResetRequest_QueuesMailInSameTransaction()
    {
        using var client = Client(); var email = Email(); await EnrollAsync(client, email);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email })).StatusCode);

        var token = await QueryAsync(db => db.PasswordResetTokens.SingleAsync(x => !x.IsUsed));
        var mail = await QueryAsync(db => db.Set<IdentityMail>().SingleAsync(x => x.PasswordResetTokenId != null));
        Assert.Equal(token.Id, mail.PasswordResetTokenId);
        Assert.Equal(token.ExpiresAt, mail.ExpiresAt);
        Assert.Equal(email, mail.Destination);

        var link = await IdentityTestMailbox.ReadPasswordResetLinkAsync(_factory, email);
        Assert.NotNull(link);
        Assert.StartsWith("https://inventory.test/reset-password?token=", link, StringComparison.Ordinal);
        var rawToken = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(link).Query)["token"].ToString();
        // The link exists only as ciphertext; the proof table keeps a hash.
        Assert.DoesNotContain(rawToken, mail.EncryptedContent, StringComparison.Ordinal);
        Assert.DoesNotContain("reset-password", mail.EncryptedContent, StringComparison.Ordinal);
        Assert.NotEqual(rawToken, token.TokenHash);
        Assert.Empty(Transport.Sent);
    }

    [Fact]
    public async Task ResetRequest_UnknownAccount_QueuesNothing()
    {
        using var client = Client();

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email = Email() })).StatusCode);

        Assert.False(await QueryAsync(db => db.Set<IdentityMail>().AnyAsync(x => x.PasswordResetTokenId != null)));
    }

    [Fact]
    public async Task PasswordResetMail_SupersededToken_IsNotSent()
    {
        using var client = Client(); var email = Email(); await EnrollAsync(client, email);
        await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email });
        await Task.Delay(10); // Distinct created_at so the worker's oldest-first order is the request order.
        await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email });
        var latest = await IdentityTestMailbox.ReadPasswordResetLinkAsync(_factory, email);

        await Worker().DeliverOneAsync(CancellationToken.None);
        await Worker().DeliverOneAsync(CancellationToken.None);

        var sent = Assert.Single(Transport.Sent);
        Assert.Contains(latest!, sent.Body, StringComparison.Ordinal);
        Assert.Equal(0, await QueryAsync(db => db.Set<IdentityMail>().CountAsync(x => x.CompletedAt == null)));
        Assert.False(await QueryAsync(db => db.Set<IdentityMail>().AnyAsync(x => x.EncryptedContent != "")));
    }

    [Fact]
    public async Task PasswordResetMail_SmtpFailure_RetriesSameLinkWithoutNewProof()
    {
        using var client = Client(); var email = Email(); await EnrollAsync(client, email);
        await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email });
        var link = await IdentityTestMailbox.ReadPasswordResetLinkAsync(_factory, email);

        Transport.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => Worker().DeliverOneAsync(CancellationToken.None));
        var pending = await QueryAsync(db => db.Set<IdentityMail>().SingleAsync(x => x.PasswordResetTokenId != null));
        Assert.Equal(1, pending.Attempts);
        Assert.Null(pending.CompletedAt);
        Assert.NotEmpty(pending.EncryptedContent);
        Assert.True(pending.NextAttemptAt > DateTimeOffset.UtcNow);

        Transport.Fail = false;
        await SqlAsync("UPDATE identity_mail SET next_attempt_at = CURRENT_TIMESTAMP - interval '1 second'");
        await Worker().DeliverOneAsync(CancellationToken.None);

        var sent = Assert.Single(Transport.Sent);
        Assert.Equal(email, sent.To);
        Assert.Equal("Reset your Intelibill password", sent.Subject);
        Assert.Contains(link!, sent.Body, StringComparison.Ordinal);
        Assert.Equal(1, await QueryAsync(db => db.PasswordResetTokens.CountAsync()));
        var delivered = await QueryAsync(db => db.Set<IdentityMail>().SingleAsync(x => x.PasswordResetTokenId != null));
        Assert.NotNull(delivered.CompletedAt);
        Assert.Empty(delivered.EncryptedContent);

        // The delivered link redeems once.
        var token = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(link!).Query)["token"].ToString();
        var confirm = new { email, token, newPassword = "RecoveredPassword!1" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/password-reset/confirm", confirm)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/password-reset/confirm", confirm)).StatusCode);
    }

    [Fact]
    public async Task PasswordResetMail_UsedBeforeDelivery_IsNeverSent()
    {
        using var client = Client(); var email = Email(); await EnrollAsync(client, email);
        await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email });
        var link = await IdentityTestMailbox.ReadPasswordResetLinkAsync(_factory, email);
        var token = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(link!).Query)["token"].ToString();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/password-reset/confirm", new { email, token, newPassword = "RecoveredPassword!1" })).StatusCode);

        // Two queued messages now: the stale reset link and the password-changed notice.
        await Worker().DeliverOneAsync(CancellationToken.None);
        await Worker().DeliverOneAsync(CancellationToken.None);

        var sent = Assert.Single(Transport.Sent);
        Assert.Equal("Your Intelibill password was changed", sent.Subject);
        Assert.DoesNotContain("reset-password", sent.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasswordChanged_NoticeGoesToVerifiedAddress()
    {
        using var client = Client(); var email = Email(); var auth = await EnrollAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());

        var changed = await client.PostAsJsonAsync("/api/users/me/change-password", new { currentPassword = Password, newPassword = "ChangedPassword!1" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var notice = await QueryAsync(db => db.Set<IdentityMail>().SingleAsync(x => x.CompletedAt == null));
        Assert.Equal(email, notice.Destination);
        Assert.Null(notice.PasswordResetTokenId);
        await Worker().DeliverOneAsync(CancellationToken.None);
        var sent = Assert.Single(Transport.Sent);
        Assert.Equal("Your Intelibill password was changed", sent.Subject);
        Assert.DoesNotContain("http", sent.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Readiness_ReflectsIdentityMailQueue()
    {
        using var client = Client(); var email = Email(); await EnrollAsync(client, email);
        Assert.Equal("Healthy", await client.GetStringAsync("/health/ready"));

        await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email });
        await SqlAsync("UPDATE identity_mail SET attempts = 5 WHERE password_reset_token_id IS NOT NULL");

        // Exhausted delivery is visible to operators but does not take the replica out of service.
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Degraded", await ready.Content.ReadAsStringAsync());
    }

    internal sealed class FlakyMailTransport : IEmailService
    {
        private readonly List<(string To, string Subject, string Body)> _sent = [];
        private readonly Lock _gate = new();
        public bool Fail { get; set; }

        public IReadOnlyList<(string To, string Subject, string Body)> Sent
        {
            get { lock (_gate) return [.. _sent]; }
        }

        public Task SendIdentityAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("Simulated SMTP outage");
            lock (_gate) _sent.Add((toEmail, subject, body));
            return Task.CompletedTask;
        }
    }
}
