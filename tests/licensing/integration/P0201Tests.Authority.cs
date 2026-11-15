using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Security;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0201Tests
{
    [Fact]
    public async Task RevokedOrChangedMerchantAuthority_EmitsNoAssertion()
    {
        await database.PrepareMerchantAsync();
        using var fixture = await P0201Fixture.CreateAsync(database);
        var merchantOwner = database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase);
        var counter = new LocalTransport(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var calls = 0;
        await using var merchant = fixture.MerchantApi(handler: () => counter, configure: services =>
            services.AddScoped<ILiveSessionAuthority>(provider =>
                new RecheckingAuthority(provider.GetRequiredService<LiveSessionValidator>(), async claims =>
                {
                    if (Interlocked.Increment(ref calls) != 2)
                        return;

                    // The first check already authenticated the HTTP request. Persist the
                    // revocation just before the issuer asks the same live authority again.
                    await LicensingPostgreSqlFixture.ExecuteAsync(merchantOwner, $"""
                        UPDATE auth_sessions SET revoked_at = now(), revocation_reason = 'fixture-revocation'
                        WHERE id = '{claims.SessionId:D}'
                        """);
                })));
        using var client = merchant.CreateClient();
        var actor = await SeedMerchantAsync(merchant.Services);
        var account = Guid.NewGuid();
        await fixture.GrantAsync(account, actor);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client));

        using var duringRequest = await client.GetAsync(MerchantPath(account));
        await AssertProblemAsync(duringRequest, HttpStatusCode.Unauthorized, "Billing.SessionInvalid");
        Assert.Equal(2, calls);
        Assert.Equal(0, counter.Calls);
        using var afterRevocation = await client.GetAsync(MerchantPath(account));
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevocation.StatusCode);
        Assert.True(afterRevocation.Headers.CacheControl?.NoStore);
        Assert.Equal(0, counter.Calls);
    }

    [Fact]
    public async Task LostPhoneVerificationBeforeSigning_EmitsNoAssertion()
    {
        await database.PrepareMerchantAsync();
        using var fixture = await P0201Fixture.CreateAsync(database);
        var owner = database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase);
        var counter = new LocalTransport(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var calls = 0;
        await using var merchant = fixture.MerchantApi(handler: () => counter, configure: services =>
            services.AddScoped<ILiveSessionAuthority>(provider =>
                new RecheckingAuthority(provider.GetRequiredService<LiveSessionValidator>(), async claims =>
                {
                    if (Interlocked.Increment(ref calls) == 2)
                        await LicensingPostgreSqlFixture.ExecuteAsync(owner, $"""
                            UPDATE users SET is_phone_verified = false, phone_verified_at = NULL
                            WHERE id = '{claims.UserId:D}'
                            """);
                })));
        using var client = merchant.CreateClient();
        var actor = await SeedMerchantAsync(merchant.Services);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client));

        // The existing merchant session rules admit a phone-only identity with no email claim
        // requirement. Billing additionally requires a current verified credential.
        await LicensingPostgreSqlFixture.ExecuteAsync(owner, $"""
            UPDATE users SET email = NULL, is_email_verified = false,
                phone_number = '+15555550101', is_phone_verified = true, phone_verified_at = now()
            WHERE id = '{actor:D}'
            """);
        try
        {
            using var response = await client.GetAsync(MerchantPath(Guid.NewGuid()));
            await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "Billing.SessionInvalid");
            Assert.Equal(2, calls);
            Assert.Equal(0, counter.Calls);
        }
        finally
        {
            await LicensingPostgreSqlFixture.ExecuteAsync(owner, $"""
                UPDATE users SET email = '{Email}', is_email_verified = true,
                    phone_number = NULL, is_phone_verified = false, phone_verified_at = NULL
                WHERE id = '{actor:D}'
                """);
        }
    }

    [Fact]
    public async Task MerchantTokensAndDelegationCredentials_AreNotInterchangeable()
    {
        await database.PrepareMerchantAsync();
        using var fixture = await P0201Fixture.CreateAsync(database);
        await using var licensing = fixture.LicensingApi();
        using var lic = licensing.CreateClient();
        await using var merchant = fixture.MerchantApi(handler: () => licensing.Server.CreateHandler());
        using var client = merchant.CreateClient();
        var actor = await SeedMerchantAsync(merchant.Services);
        var jwt = await LoginAsync(client);
        var account = Guid.NewGuid();
        await fixture.GrantAsync(account, actor);

        using var wrong = await P0201Fixture.SendAssertionAsync(lic, account, Guid.NewGuid(), jwt);
        await AssertProblemAsync(wrong, HttpStatusCode.Unauthorized, "Billing.DelegationInvalid");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            CreateAssertion(fixture, account, actor).Token);
        using var publicRoute = await client.GetAsync(MerchantPath(account));
        Assert.Equal(HttpStatusCode.Unauthorized, publicRoute.StatusCode);
        Assert.True(publicRoute.Headers.CacheControl?.NoStore);
        Assert.Equal(0L, await ReplayCountAsync(fixture));
    }

    [Fact]
    public async Task BillingOnlyMember_DoesNotRequireShopOwnership()
    {
        await database.PrepareMerchantAsync();
        using var fixture = await P0201Fixture.CreateAsync(database);
        fixture.Clock.Now = DateTimeOffset.UtcNow;
        await using var licensing = fixture.LicensingApi();
        using var licensingClient = licensing.CreateClient();
        await using var merchant = fixture.MerchantApi(handler: () => licensing.Server.CreateHandler(),
            configure: services => services.AddSingleton<TimeProvider>(fixture.Clock));
        using var client = merchant.CreateClient();
        const string staffEmail = "billing-staff-fixture@example.test";
        var actor = await SeedMerchantAsync(merchant.Services, staffEmail);
        var account = Guid.NewGuid();
        await fixture.GrantAsync(account, actor);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, staffEmail));

        var id = await SeedStaffShopAsync(actor);
        using var switched = await client.PostAsJsonAsync(new Uri("/api/shops/switch", UriKind.Relative), new { shopId = id });
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        var scope = await switched.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            scope.GetProperty("accessToken").GetString());

        using var delegated = await client.GetAsync(MerchantPath(account));
        Assert.Equal(HttpStatusCode.OK, delegated.StatusCode);
        Assert.Equal(1L, await ReplayCountAsync(fixture));
    }

    private async Task<Guid> SeedStaffShopAsync(Guid actor)
    {
        var owner = database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(owner.ConnectionString).UseSnakeCaseNamingConvention().Options;
        await using var db = new ApplicationDbContext(options);
        var shop = Shop.Create("Billing fixture shop", "Fixture Road", "Bengaluru", "Karnataka", "560001", null, null, null);
        db.Shops.Add(shop);
        db.ShopMemberships.Add(ShopMembership.Create(shop.Id, actor, ShopRole.Staff, isDefault: false));
        await db.SaveChangesAsync();
        return shop.Id;
    }

    private sealed class RecheckingAuthority(LiveSessionValidator inner, Func<SessionClaims, Task> beforeCheck)
        : ILiveSessionAuthority
    {
        public async Task<bool> IsValidAsync(SessionClaims claims, CancellationToken cancellationToken = default)
        {
            await beforeCheck(claims);
            return await inner.IsValidAsync(claims, cancellationToken);
        }
    }
}
