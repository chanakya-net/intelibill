using System.Net;
using System.Linq.Expressions;
using Intelibill.Domain.Interfaces.Repositories;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Services.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class EmailOwnershipSecurityTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture, services =>
    {
        var original = services.Single(x => x.ServiceType == typeof(IUserRepository));
        services.Remove(original);
        services.AddSingleton<ReadBarrier>();
        services.AddScoped<IUserRepository>(sp => new BarrierUsers(
            (IUserRepository)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!), sp.GetRequiredService<ReadBarrier>()));
        services.RemoveAll<IEmailService>();
        services.AddSingleton<FixtureEmailDelivery>();
        services.AddSingleton<IEmailService>(sp => sp.GetRequiredService<FixtureEmailDelivery>());
    });
    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() { _factory.Dispose(); return Task.CompletedTask; }
    public void Dispose() { _factory.Dispose(); GC.SuppressFinalize(this); }
    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
    private static string Email() => $"owner-{Guid.NewGuid():N}@test.com";
    private async Task<JsonElement> EnrollAsync(HttpClient client, string email)
    {
        var result = await _factory.EnrollAsync(client, new { email, password = "OwnersPassword!1", firstName = "Mailbox", lastName = "Owner" });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        return await result.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static void Authenticate(HttpClient client, JsonElement auth) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
    private static async Task<string> ReauthAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/reauthenticate", new { password = "OwnersPassword!1" });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("proof").GetString()!;
    }
    [Fact]
    public async Task PendingEnrollment_CannotCreateShopOrReceiveSession()
    {
        using var client = Client(); var email = Email();
        var response = await client.PostAsJsonAsync("/api/auth/enrollment/email/request", new { email });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Users.AnyAsync(x => x.Email == email));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/shops", new { name = "Unproved" })).StatusCode);
        var mail = await db.Set<IdentityMail>().SingleAsync(x => x.Destination == email);
        var proof = await IdentityTestMailbox.ReadAsync(_factory, email);
        Assert.DoesNotContain(proof.Token, mail.EncryptedContent);
    }
    [Fact]
    public async Task Confirmation_UsesPasswordChosenByMailboxOwner_AndRejectsReplay()
    {
        using var client = Client(); var email = Email();
        var request = await client.PostAsJsonAsync("/api/auth/register/email", new { email, password = "AttackerPassword!1", firstName = "Attacker", lastName = "Name", phoneNumber = "+919876543210" });
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var (id, token) = await IdentityTestMailbox.ReadAsync(_factory, email);
        var confirmation = new { challengeId = id, token, password = "OwnersPassword!1", firstName = "Real", lastName = "Owner" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/enrollment/email/confirm", confirmation)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/enrollment/email/confirm", confirmation)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { identifier = email, password = "AttackerPassword!1" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { identifier = email, password = "OwnersPassword!1" })).StatusCode);
    }
    [Fact]
    public async Task ConcurrentConfirmation_OnlyOneCreatesSession()
    {
        using var client = Client(); var email = Email();
        await client.PostAsJsonAsync("/api/auth/enrollment/email/request", new { email });
        var (id, token) = await IdentityTestMailbox.ReadAsync(_factory, email);
        var request = new { challengeId = id, token, password = "OwnersPassword!1", firstName = "Real", lastName = "Owner" };
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/api/auth/enrollment/email/confirm", request)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, responses.Count(x => x.StatusCode == HttpStatusCode.BadRequest));
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(x => x.Email == email));
        Assert.Equal(1, await db.RefreshTokens.CountAsync());
    }
    [Fact]
    public async Task Resend_RacesShareDestinationBudget_AndSupersedeOldProof()
    {
        using var client = Client(); var email = Email();
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/api/auth/enrollment/email/request", new { email })));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.Accepted, x.StatusCode));
        var old = await IdentityTestMailbox.ReadAsync(_factory, email);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.Set<IdentityMail>().CountAsync());
            await db.Database.ExecuteSqlRawAsync("UPDATE identity_mail SET created_at = created_at - interval '2 minutes'");
        }
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/enrollment/email/request", new { email })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/enrollment/email/confirm", new { challengeId = old.Id, token = old.Token, password = "OwnersPassword!1", firstName = "Real", lastName = "Owner" })).StatusCode);
    }
    [Fact]
    public async Task ClaimLegacyUnverifiedAccount_InvalidatesOldCredentials()
    {
        using var client = Client(); var email = Email(); Guid userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = User.CreateWithEmail(email, scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash("AttackerPassword!1"), "Attacker", "Name");
            userId = user.Id; db.Users.Add(user);
            db.RefreshTokens.Add(RefreshToken.Create(user.Id, "attacker-refresh", DateTimeOffset.UtcNow.AddDays(1)));
            await db.SaveChangesAsync();
        }
        await client.PostAsJsonAsync("/api/auth/enrollment/email/claim/request", new { email });
        var (id, token) = await IdentityTestMailbox.ReadAsync(_factory, email, "claim");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/enrollment/email/claim/confirm", new { challengeId = id, token, password = "OwnersPassword!1", firstName = "Real", lastName = "Owner" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/token/refresh", new { refreshToken = "attacker-refresh" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { identifier = email, password = "AttackerPassword!1" })).StatusCode);
        using var verify = _factory.Services.CreateScope(); var database = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True((await database.Users.SingleAsync(x => x.Id == userId)).IsEmailVerified);
    }
    [Fact]
    public async Task EmailChange_ConcurrentConfirm_OnlyOneWins_AndRevokesEverySession()
    {
        using var client = Client(); var email = Email(); var destination = Email();
        var auth = await EnrollAsync(client, email); Authenticate(client, auth);
        var proof = await ReauthAsync(client);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/email-change/request", new { email = destination, reauthProof = proof })).StatusCode);
        using (var scope = _factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AnyAsync(x => x.Email == email && x.IsEmailVerified));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/email-change/request", new { email = Email(), reauthProof = proof })).StatusCode);
        var challenge = await IdentityTestMailbox.ReadAsync(_factory, destination, "email-change");
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync("/api/auth/email-change/confirm", new { challengeId = challenge.Id, token = challenge.Token })));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/sign-in-methods")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/token/refresh", new { refreshToken = auth.GetProperty("refreshToken").GetString() })).StatusCode);
        using var verify = _factory.Services.CreateScope(); var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.Set<IdentityMail>().CountAsync(x => x.ChallengeId == null));
    }
    [Fact]
    public async Task LegacyBusinessAccount_DeletedMembershipStillRequiresSupport()
    {
        using var client = Client(); var email = Email(); Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = User.CreateWithEmail(email, "untrusted", "Legacy", "Owner"); id = user.Id;
            var shop = Shop.Create("Existing business", "Address", "City", "State", "560001", null, null, null);
            db.Users.Add(user); db.Shops.Add(shop);
            db.ShopMemberships.Add(ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, true));
            await db.SaveChangesAsync();
            await db.ShopMemberships.Where(x => x.UserId == id).ExecuteDeleteAsync();
        }
        await client.PostAsJsonAsync("/api/auth/enrollment/email/claim/request", new { email });
        var challenge = await IdentityTestMailbox.ReadAsync(_factory, email, "claim");
        var response = await client.PostAsJsonAsync("/api/auth/enrollment/email/claim/confirm", new { challengeId = challenge.Id, token = challenge.Token, password = "OwnersPassword!1", firstName = "Real", lastName = "Owner" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Auth.RecoverySupportRequired", await response.Content.ReadAsStringAsync());
        using var verify = _factory.Services.CreateScope(); var database = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await database.Shops.CountAsync());
        Assert.False((await database.Users.SingleAsync(x => x.Id == id)).IsEmailVerified);
    }
    [Fact]
    public async Task PhoneOnlyAccount_CanEditProfileWithoutEmail()
    {
        using var client = Client();
        var registration = await client.PostAsJsonAsync("/api/auth/register/phone", new { phoneNumber = "+9199" + Random.Shared.Next(10000000, 99999999), firstName = "Phone", lastName = "Owner" });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        Authenticate(client, await registration.Content.ReadFromJsonAsync<JsonElement>());
        var profile = await client.PutAsJsonAsync("/api/users/me", new { email = (string?)null, phoneNumber = (string?)null, firstName = "Updated", lastName = "User", language = "en-IN" });
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
    }
    [Fact]
    public async Task ExpiredProof_CannotCreateUser()
    {
        using var client = Client(); var email = Email();
        await client.PostAsJsonAsync("/api/auth/enrollment/email/request", new { email });
        var challenge = await IdentityTestMailbox.ReadAsync(_factory, email);
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlRawAsync("UPDATE email_ownership_challenges SET expires_at = CURRENT_TIMESTAMP - interval '1 second'");
        var response = await client.PostAsJsonAsync("/api/auth/enrollment/email/confirm", new { challengeId = challenge.Id, token = challenge.Token, password = "OwnersPassword!1", firstName = "Real", lastName = "Owner" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task NormalizedUniqueEmail_ConcurrentDistinctChallenges_OnlyOneAccount()
    {
        using var client = Client(); var email = Email();
        var requests = await Task.WhenAll(client.PostAsJsonAsync("/api/auth/enrollment/email/request", new { email }), client.PostAsJsonAsync("/api/auth/enrollment/email/request", new { email = email.ToUpperInvariant() }));
        Assert.All(requests, x => Assert.Equal(HttpStatusCode.Accepted, x.StatusCode));
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Set<EmailOwnershipChallenge>().CountAsync());
        var challenge = await IdentityTestMailbox.ReadAsync(_factory, email);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/enrollment/email/confirm", new { challengeId = challenge.Id, token = challenge.Token, password = "OwnersPassword!1", firstName = "Real", lastName = "Owner" })).StatusCode);
        var duplicate = User.CreateWithEmail(email.ToUpperInvariant(), "hash", "Duplicate", "User"); duplicate.VerifyEmail(email);
        db.Users.Add(duplicate); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task EmailChange_VictimClickAndDifferentActor_CannotActivateAttackerCredentials()
    {
        using var attacker = Client(); using var victim = Client(); using var anonymous = Client();
        var attackerEmail = Email(); var victimMailbox = Email();
        Authenticate(attacker, await EnrollAsync(attacker, attackerEmail));
        Authenticate(victim, await EnrollAsync(victim, Email()));
        var proof = await ReauthAsync(attacker);
        Assert.Equal(HttpStatusCode.Accepted, (await attacker.PostAsJsonAsync("/api/auth/email-change/request", new { email = victimMailbox, reauthProof = proof })).StatusCode);
        var challenge = await IdentityTestMailbox.ReadAsync(_factory, victimMailbox, "email-change");
        var confirmation = new { challengeId = challenge.Id, token = challenge.Token };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/email-change/confirm", confirmation)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await victim.PostAsJsonAsync("/api/auth/email-change/confirm", confirmation)).StatusCode);
        using var verify = _factory.Services.CreateScope(); var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null((await db.Set<EmailOwnershipChallenge>().SingleAsync(x => x.Id == challenge.Id)).ConsumedAt);
        Assert.True(await db.Users.AnyAsync(x => x.Email == attackerEmail));
        Assert.False(await db.Users.AnyAsync(x => x.Email == victimMailbox));
    }

    [Fact]
    public async Task DurableOutbox_RetrySurvivesWorkerRestart_AndErasesCiphertextAfterDelivery()
    {
        using var client = Client(); var email = Email();
        await client.PostAsJsonAsync("/api/auth/enrollment/email/request", new { email });
        var delivery = _factory.Services.GetRequiredService<FixtureEmailDelivery>();
        delivery.Fail = true;
        var worker = new IdentityMailWorker(_factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<IdentityMailWorker>.Instance);
        await Assert.ThrowsAsync<IOException>(() => worker.DeliverOneAsync(CancellationToken.None));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var queued = await db.Set<IdentityMail>().SingleAsync();
            Assert.Equal(1, queued.Attempts); Assert.Null(queued.CompletedAt); Assert.NotEmpty(queued.EncryptedContent);
            await db.Database.ExecuteSqlRawAsync("UPDATE identity_mail SET next_attempt_at = CURRENT_TIMESTAMP - interval '1 second'");
        }
        delivery.Fail = false;
        var restarted = new IdentityMailWorker(_factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<IdentityMailWorker>.Instance);
        await restarted.DeliverOneAsync(CancellationToken.None);
        using var verify = _factory.Services.CreateScope();
        var completed = await verify.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<IdentityMail>().SingleAsync();
        Assert.Equal(2, completed.Attempts); Assert.NotNull(completed.CompletedAt); Assert.Empty(completed.EncryptedContent);
        Assert.Equal(1, delivery.Delivered);
    }
    [Fact]
    public async Task StaleCredentialSnapshot_CannotMintCurrentVersionToken()
    {
        using var client = Client(); var email = Email(); await EnrollAsync(client, email);
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stale = await db.Users.SingleAsync();
        using (var concurrent = _factory.Services.CreateScope())
            await concurrent.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlRawAsync("UPDATE users SET security_version = security_version + 1");
        var session = Intelibill.Domain.Entities.AuthSession.Start(
            stale.Id, stale.SecurityVersion, "1", null, Intelibill.Domain.ValueObjects.DeviceMetadata.Unknown,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessTokenAsync(stale, session, null));
    }
    [Fact]
    public async Task ReauthenticationProof_BelongsToActor()
    {
        using var first = Client(); using var second = Client();
        Authenticate(first, await EnrollAsync(first, Email()));
        Authenticate(second, await EnrollAsync(second, Email()));
        var proof = await ReauthAsync(first);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.PostAsJsonAsync("/api/auth/email-change/request", new { email = Email(), reauthProof = proof })).StatusCode);
    }
    [Theory]
    [InlineData("profile")]
    [InlineData("create")]
    [InlineData("switch")]
    [InlineData("default")]
    public async Task AuthenticatedIssuer_InvalidatedBeforeUserLoad_CannotUpgradeBearer(string operation)
    {
        using var client = Client(); var email = Email(); Authenticate(client, await EnrollAsync(client, email));
        var shopRequest = new { name = "Shop", address = "Street", city = "City", state = "State", pincode = "560001" };
        var created = await client.PostAsJsonAsync("/api/shops", shopRequest);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var auth = await created.Content.ReadFromJsonAsync<JsonElement>(); Authenticate(client, auth);
        var shopId = auth.GetProperty("activeShopId").GetGuid();
        var barrier = _factory.Services.GetRequiredService<ReadBarrier>(); barrier.BeforeId = true;
        var pending = operation switch
        {
            "profile" => client.PutAsJsonAsync("/api/users/me", new { email, firstName = "Updated", lastName = "Owner", language = "en-IN" }),
            "create" => client.PostAsJsonAsync("/api/shops", shopRequest),
            "switch" => client.PostAsJsonAsync("/api/shops/switch", new { shopId }),
            _ => client.PostAsJsonAsync("/api/shops/default", new { shopId })
        };
        await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
        using (var concurrent = _factory.Services.CreateScope())
            await concurrent.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlRawAsync("UPDATE users SET security_version = security_version + 1");
        barrier.Release.TrySetResult();
        Assert.Equal(HttpStatusCode.Unauthorized, (await pending).StatusCode);
    }

    [Fact]
    public async Task ResetRequest_AfterConcurrentEmailChange_CannotIssueOldMailboxAuthority()
    {
        using var client = Client(); var email = Email(); var destination = Email(); Authenticate(client, await EnrollAsync(client, email));
        var proof = await ReauthAsync(client);
        await client.PostAsJsonAsync("/api/auth/email-change/request", new { email = destination, reauthProof = proof });
        var challenge = await IdentityTestMailbox.ReadAsync(_factory, destination, "email-change");
        var barrier = _factory.Services.GetRequiredService<ReadBarrier>(); barrier.AfterEmail = true;
        var pending = client.PostAsJsonAsync("/api/auth/password-reset/request", new { email });
        await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/email-change/confirm", new { challengeId = challenge.Id, token = challenge.Token })).StatusCode);
        barrier.Release.TrySetResult();
        Assert.Equal(HttpStatusCode.OK, (await pending).StatusCode);
        var delivery = _factory.Services.GetRequiredService<FixtureEmailDelivery>();
        if (delivery.ResetLink is not null)
        {
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(delivery.ResetLink).Query);
            var reset = await client.PostAsJsonAsync("/api/auth/password-reset/confirm", new { email = destination, token = query["token"].ToString(), newPassword = "StolenPassword!1" });
            Assert.Equal(HttpStatusCode.Unauthorized, reset.StatusCode);
        }
        using var scope = _factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PasswordResetTokens.AnyAsync(x => !x.IsUsed));
    }

    [Fact]
    public async Task BusinessHistory_StaleWholeEntityWrite_CannotEraseClaimRestriction()
    {
        using var client = Client(); var email = Email();
        using var staleScope = _factory.Services.CreateScope(); var staleDb = staleScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = User.CreateWithEmail(email, "untrusted", "Legacy", "Owner"); staleDb.Add(user); await staleDb.SaveChangesAsync();
        Assert.False(user.RequiresOperatorRecovery);
        using (var other = _factory.Services.CreateScope())
        {
            var db = other.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var shop = Shop.Create("History", "Address", "City", "State", "560001", null, null, null);
            db.Add(shop); db.Add(ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, true)); await db.SaveChangesAsync();
            await db.ShopMemberships.Where(x => x.UserId == user.Id).ExecuteDeleteAsync();
        }
        user.UpdateProfile(email, null, "Stale", "Profile", "en-IN"); staleDb.Update(user); await staleDb.SaveChangesAsync();
        using (var verify = _factory.Services.CreateScope())
            Assert.True((await verify.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.SingleAsync()).RequiresOperatorRecovery);
        await client.PostAsJsonAsync("/api/auth/enrollment/email/claim/request", new { email });
        var challenge = await IdentityTestMailbox.ReadAsync(_factory, email, "claim");
        var response = await client.PostAsJsonAsync("/api/auth/enrollment/email/claim/confirm", new { challengeId = challenge.Id, token = challenge.Token, password = "OwnersPassword!1", firstName = "New", lastName = "Owner" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Claim_ConcurrentMembershipHistory_IsCheckedAfterStaleUserRead()
    {
        using var client = Client(); var email = Email(); Guid userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = User.CreateWithEmail(email, "untrusted", "Legacy", "Owner"); userId = user.Id;
            db.Add(user); await db.SaveChangesAsync();
        }
        await client.PostAsJsonAsync("/api/auth/enrollment/email/claim/request", new { email });
        var challenge = await IdentityTestMailbox.ReadAsync(_factory, email, "claim");
        var barrier = _factory.Services.GetRequiredService<ReadBarrier>(); barrier.AfterEmail = true;
        var pending = client.PostAsJsonAsync("/api/auth/enrollment/email/claim/confirm", new { challengeId = challenge.Id, token = challenge.Token, password = "OwnersPassword!1", firstName = "New", lastName = "Owner" });
        await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
        using (var other = _factory.Services.CreateScope())
        {
            var db = other.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var shop = Shop.Create("History", "Address", "City", "State", "560001", null, null, null);
            db.Add(shop); db.Add(ShopMembership.Create(shop.Id, userId, ShopRole.Owner, true)); await db.SaveChangesAsync();
            await db.ShopMemberships.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        }
        barrier.Release.TrySetResult();
        Assert.Equal(HttpStatusCode.Conflict, (await pending).StatusCode);
        using var verify = _factory.Services.CreateScope(); var database = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False((await database.Users.SingleAsync()).IsEmailVerified);
        Assert.Null((await database.Set<EmailOwnershipChallenge>().SingleAsync(x => x.Id == challenge.Id)).ConsumedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResetProof_MustMatchCurrentVersionAndDestination(bool staleVersion)
    {
        using var client = Client(); var email = Email(); await EnrollAsync(client, email);
        const string rawToken = "old-mailbox-proof";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var user = await db.Users.SingleAsync();
            var hash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));
            db.Add(PasswordResetToken.Create(user.Id, hash, securityVersion: staleVersion ? user.SecurityVersion - 1 : user.SecurityVersion, destination: staleVersion ? email : "old@test.com"));
            await db.SaveChangesAsync();
        }
        var response = await client.PostAsJsonAsync("/api/auth/password-reset/confirm", new { email, token = rawToken, newPassword = "StolenPassword!1" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { identifier = email, password = "OwnersPassword!1" })).StatusCode);
    }

    [Fact]
    public async Task ResetDeliveryFailure_KnownAndUnknownAddresses_KeepGenericResponse()
    {
        using var client = Client(); var email = Email(); await EnrollAsync(client, email);
        _factory.Services.GetRequiredService<FixtureEmailDelivery>().Fail = true;
        var known = await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email });
        var unknown = await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email = Email() });
        Assert.Equal(HttpStatusCode.OK, known.StatusCode); Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ResetProof_ConcurrentRedemption_OnlyOneCommitsAndRevokesSession()
    {
        using var client = Client(); var email = Email(); var auth = await EnrollAsync(client, email);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email })).StatusCode);
        var link = _factory.Services.GetRequiredService<FixtureEmailDelivery>().ResetLink!;
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(link).Query);
        var request = new { email, token = query["token"].ToString(), newPassword = "UpdatedPassword!1" };
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync("/api/auth/password-reset/confirm", request)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Unauthorized);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { identifier = email, password = "UpdatedPassword!1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/token/refresh", new { refreshToken = auth.GetProperty("refreshToken").GetString() })).StatusCode);
    }

    public sealed class ReadBarrier
    {
        public bool BeforeId { get; set; }
        public bool AfterEmail { get; set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task PauseAsync() { Reached.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
    }
    private sealed class BarrierUsers(IUserRepository inner, ReadBarrier barrier) : IUserRepository
    {
        public async Task<User?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
        { if (barrier.BeforeId) { barrier.BeforeId = false; await barrier.PauseAsync(); } return await inner.GetByIdWithDetailsAsync(id, ct); }
        public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
        { var user = await inner.GetByEmailAsync(email, ct); if (barrier.AfterEmail) { barrier.AfterEmail = false; await barrier.PauseAsync(); } return user; }
        public Task<User?> GetByPhoneAsync(string phone, CancellationToken ct = default) => inner.GetByPhoneAsync(phone, ct);
        public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default) => inner.ExistsByEmailAsync(email, ct);
        public Task<bool> ExistsByPhoneAsync(string phone, CancellationToken ct = default) => inner.ExistsByPhoneAsync(phone, ct);
        public Task<bool> ExistsByPhoneAsync(string phone, Guid id, CancellationToken ct = default) => inner.ExistsByPhoneAsync(phone, id, ct);
        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) => inner.GetAllAsync(ct);
        public Task<IReadOnlyList<User>> FindAsync(Expression<Func<User, bool>> predicate, CancellationToken ct = default) => inner.FindAsync(predicate, ct);
        public Task AddAsync(User user, CancellationToken ct = default) => inner.AddAsync(user, ct);
        public void Update(User user) => inner.Update(user);
        public void Remove(User user) => inner.Remove(user);
    }
    public sealed class FixtureEmailDelivery : IEmailService
    {
        public bool Fail { get; set; }
        public int Delivered { get; private set; }
        public string? ResetLink { get; private set; }
        public Task SendPasswordResetAsync(string toEmail, string resetLink, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) { if (Fail) throw new IOException("Simulated SMTP outage"); ResetLink = resetLink; return Task.CompletedTask; }
        public Task SendIdentityAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("Simulated SMTP outage");
            Delivered++; return Task.CompletedTask;
        }
    }
}
