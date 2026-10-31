using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class PhoneLifecycleIntegrationTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture);

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() { _factory.Dispose(); return Task.CompletedTask; }
    public void Dispose() { _factory.Dispose(); GC.SuppressFinalize(this); }

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
    private static string Phone() => $"+919{Random.Shared.NextInt64(100_000_000, 999_999_999)}";

    /// <summary>
    /// The journey the market readiness review calls a dead end today: create an account on a
    /// proved number, lose the session, and come back on another device with fresh proof.
    /// </summary>
    [Fact]
    public async Task PhoneSignup_Logout_NewDeviceLogin_SucceedsAfterNewProof()
    {
        var phone = Phone();
        using var first = Client();

        var enrolled = await _factory.EnrollPhoneAsync(first, phone);
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        var signup = await enrolled.Content.ReadFromJsonAsync<JsonElement>();
        var firstSession = signup.GetProperty("sessionId").GetString();

        var signedOut = await first.PostAsJsonAsync("/api/auth/token/revoke", new { refreshToken = signup.GetProperty("refreshToken").GetString() });
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);

        // The enrollment code went to this number moments ago, and the resend spacing is a durable
        // per-destination rule. A returning user would simply have waited.
        await AgeMessagesAsync();

        using var second = Client();
        var requested = await second.PostAsJsonAsync("/api/auth/phone/login/request", new { phoneNumber = phone });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var receipt = await requested.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(Guid.Empty, receipt.GetProperty("challengeId").GetGuid());

        var (challengeId, code) = await IdentityTestPhone.ReadCodeAsync(_factory, phone, PhoneChallengePurposes.Login);
        var loggedIn = await second.PostAsJsonAsync("/api/auth/phone/login/confirm", new { challengeId, code });

        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
        var session = await loggedIn.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(session.GetProperty("accessToken").GetString()));
        Assert.NotEqual(firstSession, session.GetProperty("sessionId").GetString());
        Assert.Equal(phone, session.GetProperty("user").GetProperty("phoneNumber").GetString());
    }

    /// <summary>
    /// One delivered code, two confirmations at once. The challenge is read FOR UPDATE inside the
    /// identity lock, so the second one sees the first one's consumption and the account ends up
    /// with a single device.
    /// </summary>
    [Fact]
    public async Task Otp_ParallelConfirm_CreatesOneSession()
    {
        var phone = Phone();
        using var client = Client();

        var requested = await client.PostAsJsonAsync("/api/auth/phone/enrollment/request", new { phoneNumber = phone });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var (challengeId, code) = await IdentityTestPhone.ReadCodeAsync(_factory, phone);

        var body = new { challengeId, code, firstName = "Phone", lastName = "Owner" };
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/auth/phone/enrollment/confirm", body),
            client.PostAsJsonAsync("/api/auth/phone/enrollment/confirm", body));

        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode != HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(x => x.PhoneNumber == phone);
        Assert.True(user.IsPhoneVerified);
        Assert.Equal(1, await db.AuthSessions.CountAsync(x => x.UserId == user.Id));
    }

    [Fact]
    public async Task Otp_ExpiredOrFiveWrongAttempts_CannotAuthenticate()
    {
        using var client = Client();

        var expiredPhone = Phone();
        await client.PostAsJsonAsync("/api/auth/phone/enrollment/request", new { phoneNumber = expiredPhone });
        var expired = await IdentityTestPhone.ReadCodeAsync(_factory, expiredPhone);
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database
                .ExecuteSqlRawAsync("UPDATE phone_challenges SET expires_at = CURRENT_TIMESTAMP - interval '1 second'");
        }

        var afterExpiry = await Confirm(client, expired.Id, expired.Code);
        Assert.Equal(HttpStatusCode.BadRequest, afterExpiry.StatusCode);

        var guessedPhone = Phone();
        await client.PostAsJsonAsync("/api/auth/phone/enrollment/request", new { phoneNumber = guessedPhone });
        var live = await IdentityTestPhone.ReadCodeAsync(_factory, guessedPhone);

        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, live.Id, WrongCode(live.Code))).StatusCode);

        // The budget is spent, so even the right code is refused now.
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, live.Id, live.Code)).StatusCode);

        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Users.AnyAsync(x => x.PhoneNumber == guessedPhone || x.PhoneNumber == expiredPhone));
        Assert.Equal(5, (await db.Set<PhoneChallenge>().SingleAsync(x => x.Id == live.Id)).Attempts);
    }

    /// <summary>
    /// Two codes must never both work: a resend closes its predecessor, so which code is live is
    /// never ambiguous.
    /// </summary>
    [Fact]
    public async Task Otp_ResendInvalidatesPriorCode()
    {
        var phone = Phone();
        using var client = Client();

        await client.PostAsJsonAsync("/api/auth/phone/enrollment/request", new { phoneNumber = phone });
        var original = await IdentityTestPhone.ReadCodeAsync(_factory, phone);

        // The resend spacing is a durable rule, so it is moved out of the way rather than waited on.
        await AgeMessagesAsync();

        await client.PostAsJsonAsync("/api/auth/phone/enrollment/request", new { phoneNumber = phone });
        var replacement = await IdentityTestPhone.ReadCodeAsync(_factory, phone);
        Assert.NotEqual(original.Id, replacement.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, original.Id, original.Code)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Confirm(client, replacement.Id, replacement.Code)).StatusCode);
    }

    [Fact]
    public async Task Enroll_AmbiguousNumber_IsRejectedNotRewritten()
    {
        using var client = Client();

        foreach (var ambiguous in new[] { "0987654321", "1234567890", "98765" })
        {
            var response = await client.PostAsJsonAsync("/api/auth/phone/enrollment/request", new { phoneNumber = ambiguous });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Auth.InvalidPhoneNumber", await response.Content.ReadAsStringAsync());
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.Set<PhoneChallenge>().CountAsync());
        Assert.Equal(0, await db.Set<IdentitySms>().CountAsync());
    }

    /// <summary>
    /// The receipt goes to an unauthenticated caller, so it carries a masked destination and never
    /// the code. The code exists only in the encrypted outbox row.
    /// </summary>
    [Fact]
    public async Task Receipt_MasksDestinationAndOmitsCode()
    {
        var phone = Phone();
        using var client = Client();

        var response = await client.PostAsJsonAsync("/api/auth/phone/enrollment/request", new { phoneNumber = phone });
        var body = await response.Content.ReadAsStringAsync();
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>();
        var (_, code) = await IdentityTestPhone.ReadCodeAsync(_factory, phone);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain(phone, body, StringComparison.Ordinal);
        Assert.DoesNotContain(code, body, StringComparison.Ordinal);
        Assert.Contains("•", receipt.GetProperty("maskedDestination").GetString()!, StringComparison.Ordinal);
        Assert.EndsWith(phone[^3..], receipt.GetProperty("maskedDestination").GetString()!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A number somebody typed into their profile is contact information, not an identity. Proving
    /// it gives the prover their own new account; it never hands them the account that listed it.
    /// </summary>
    [Fact]
    public async Task Enroll_ExistingGlobalIdentity_IsNotMergedByUnverifiedContactPhone()
    {
        var phone = Phone();
        using var client = Client();

        var email = $"contact-{Guid.NewGuid():N}@test.com";
        var owner = await _factory.EnrollAsync(client, new { email, password = "OwnersPassword!1", firstName = "Contact", lastName = "Lister" });
        owner.EnsureSuccessStatusCode();
        var ownerAuth = await owner.Content.ReadFromJsonAsync<JsonElement>();
        var ownerId = ownerAuth.GetProperty("user").GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Users.Where(x => x.Id == ownerId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.PhoneNumber, phone));
        }

        using var claimant = Client();
        var enrolled = await _factory.EnrollPhoneAsync(claimant, phone, "New", "Holder");
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        var session = await enrolled.Content.ReadFromJsonAsync<JsonElement>();

        Assert.NotEqual(ownerId, session.GetProperty("user").GetProperty("id").GetGuid());

        using var verify = _factory.Services.CreateScope();
        var database = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var lister = await database.Users.SingleAsync(x => x.Id == ownerId);
        Assert.Null(lister.PhoneNumber);
        Assert.True(lister.IsEmailVerified);
        Assert.Equal(email, lister.Email);
    }

    /// <summary>
    /// Backdates queued messages so the durable per-destination send budget and resend spacing do
    /// not make a test wait a real minute for behaviour that is not about waiting.
    /// </summary>
    private async Task AgeMessagesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database
            .ExecuteSqlRawAsync("UPDATE identity_sms SET created_at = created_at - interval '10 minutes'");
    }

    private static Task<HttpResponseMessage> Confirm(HttpClient client, Guid challengeId, string code) =>
        client.PostAsJsonAsync("/api/auth/phone/enrollment/confirm", new { challengeId, code, firstName = "Phone", lastName = "Owner" });

    private static string WrongCode(string code) => code[0] == '0' ? "1" + code[1..] : "0" + code[1..];
}

/// <summary>
/// With phone authentication switched off, the switch has to close the challenge endpoints too.
/// Closing only the legacy route would ship a worse hole than the one being closed.
/// </summary>
[Collection("Integration Tests")]
public sealed class PhoneAuthDisabledIntegrationTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture, enablePhoneAuth: false);

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() { _factory.Dispose(); return Task.CompletedTask; }
    public void Dispose() { _factory.Dispose(); GC.SuppressFinalize(this); }

    [Fact]
    public async Task PhoneAuthDisabled_ChallengeEndpointsAlsoRefuse()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var phone = "+919876543210";

        var availability = await client.GetFromJsonAsync<JsonElement>("/api/auth/phone/availability");
        Assert.False(availability.GetProperty("enabled").GetBoolean());

        var routes = new (string Route, object Body)[]
        {
            ("/api/auth/register/phone", new { phoneNumber = phone, firstName = "Phone", lastName = "Owner" }),
            ("/api/auth/phone/enrollment/request", new { phoneNumber = phone }),
            ("/api/auth/phone/login/request", new { phoneNumber = phone }),
        };

        foreach (var (route, body) in routes)
        {
            var response = await client.PostAsJsonAsync(route, body);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("Auth.PhoneEnrollmentUnavailable", await response.Content.ReadAsStringAsync());
        }

        var confirm = await client.PostAsJsonAsync("/api/auth/phone/enrollment/confirm",
            new { challengeId = Guid.NewGuid(), code = "123456", firstName = "Phone", lastName = "Owner" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, confirm.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.Users.CountAsync());
        Assert.Equal(0, await db.Set<PhoneChallenge>().CountAsync());
    }
}
