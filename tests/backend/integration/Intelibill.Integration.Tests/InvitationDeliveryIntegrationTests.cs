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
using Microsoft.Extensions.Logging;

namespace Intelibill.Integration.Tests;

/// <summary>
/// The outbox worker is removed from the default integration host, so the rest of the suite reads
/// invitation tokens straight out of the encrypted outbox row and never exercises delivery. These
/// tests drive the worker directly, which is the only place a queued invitation becomes an email.
/// </summary>
[Collection("Integration Tests")]
public sealed class InvitationDeliveryIntegrationTests : IAsyncLifetime, IDisposable
{
    private readonly RecordingEmailService _email = new();
    private readonly ApiWebApplicationFactory _factory;

    public InvitationDeliveryIntegrationTests(PostgreSqlTestFixture fixture)
    {
        var email = _email;
        _factory = new ApiWebApplicationFactory(fixture, services =>
        {
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(email);
        });
    }

    public Task InitializeAsync() => _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
    });

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@test.com";

    private static string UniquePhone() => $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}";

    private static async Task<(Guid ShopId, string OwnerToken)> CreateOwnerWithShopAsync(HttpClient client)
    {
        var email = UniqueEmail("owner");
        var enrolled = await client.EnrollAsync(new
        {
            email,
            password = "Pass123!Aa",
            firstName = "Verified",
            lastName = "Owner",
            phoneNumber = UniquePhone(),
        });
        enrolled.EnsureSuccessStatusCode();
        var token = (await enrolled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("activeShopId").GetGuid(), body.GetProperty("accessToken").GetString()!);
    }

    private static async Task<HttpResponseMessage> InviteAsync(HttpClient client, string ownerToken, Guid shopId, string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/shops/{shopId}/invitations");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new { email, role = "Manager" });
        return await client.SendAsync(request);
    }

    /// <summary>
    /// Drains the outbox. The default integration host removes the worker as a hosted service, so
    /// it is constructed here against the host's own scope factory. It handles one message per
    /// tick, and enrolling the owner already queued a message, so this runs until the queue is
    /// empty rather than once.
    /// </summary>
    private async Task DeliverQueuedMailAsync()
    {
        var worker = new IdentityMailWorker(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Services.GetRequiredService<ILogger<IdentityMailWorker>>());

        for (var tick = 0; tick < 20 && await HasDeliverableMailAsync(); tick++)
        {
            await worker.DeliverOneAsync(CancellationToken.None);
        }

        Assert.False(await HasDeliverableMailAsync(), "The outbox did not drain.");
    }

    private async Task<bool> HasDeliverableMailAsync()
    {
        var now = DateTimeOffset.UtcNow;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Set<IdentityMail>().AsNoTracking()
            .AnyAsync(x => x.CompletedAt == null && x.NextAttemptAt <= now && x.Attempts < 5);
    }

    [Fact]
    public async Task QueuedInvitation_IsActuallyDelivered()
    {
        using var client = CreateClient();
        var (shopId, ownerToken) = await CreateOwnerWithShopAsync(client);
        var invited = UniqueEmail("invited");

        Assert.Equal(HttpStatusCode.Created, (await InviteAsync(client, ownerToken, shopId, invited)).StatusCode);

        await DeliverQueuedMailAsync();

        var sent = Assert.Single(_email.Sent, message => message.To == invited);
        Assert.Contains("/invitations/accept", sent.Body, StringComparison.Ordinal);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mail = await db.Set<IdentityMail>().AsNoTracking().SingleAsync(x => x.Destination == invited);
        Assert.NotNull(mail.CompletedAt);
        Assert.NotNull(mail.InvitationId);
        Assert.Null(mail.ChallengeId);
    }

    [Fact]
    public async Task InvitingAnAddress_DoesNotConsumeThatPersonsOwnEnrollmentBudget()
    {
        using var client = CreateClient();
        var (shopId, ownerToken) = await CreateOwnerWithShopAsync(client);
        var invited = UniqueEmail("invited");

        Assert.Equal(HttpStatusCode.Created, (await InviteAsync(client, ownerToken, shopId, invited)).StatusCode);

        // The invited person now enrolls on their own. Being invited must not have spent the
        // per-destination budget that protects their enrollment and recovery mail.
        var requested = await client.PostAsJsonAsync("/api/auth/enrollment/email/request", new { email = invited });
        requested.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(
            await db.Set<EmailOwnershipChallenge>()
                .AnyAsync(x => x.Destination == invited && x.Purpose == "enrollment"),
            "Being invited must not suppress the invited person's own enrollment challenge.");
    }

    [Fact]
    public async Task WithdrawnInvitation_IsNotDelivered()
    {
        using var client = CreateClient();
        var (shopId, ownerToken) = await CreateOwnerWithShopAsync(client);
        var invited = UniqueEmail("invited");

        var created = await InviteAsync(client, ownerToken, shopId, invited);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var invitationId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("invitationId").GetGuid();

        using var revoke = new HttpRequestMessage(HttpMethod.Delete, $"/api/shops/{shopId}/invitations/{invitationId}");
        revoke.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(revoke)).StatusCode);

        await DeliverQueuedMailAsync();

        Assert.DoesNotContain(_email.Sent, message => message.To == invited);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mail = await db.Set<IdentityMail>().AsNoTracking().SingleAsync(x => x.Destination == invited);
        Assert.NotNull(mail.CompletedAt);
    }
}

internal sealed class RecordingEmailService : IEmailService
{
    private readonly List<(string To, string Subject, string Body)> _sent = [];
    private readonly object _gate = new();

    public IReadOnlyList<(string To, string Subject, string Body)> Sent
    {
        get
        {
            lock (_gate)
            {
                return [.. _sent];
            }
        }
    }

    public Task SendPasswordResetAsync(string toEmail, string resetLink, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SendIdentityAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _sent.Add((toEmail, subject, body));
        }

        return Task.CompletedTask;
    }
}
