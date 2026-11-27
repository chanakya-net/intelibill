using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Billing;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0414LicensingTests
{
    private async Task<(SoldSale Sale, Guid Root, uint Version)> CreateReleasableHoldAsync(Shop shop)
    {
        var sale = await SellGoodsAsync(shop, quantity: 5m, missingDate: true);
        using var policy = await SendAsync(shop, HttpMethod.Put, "/api/expiry-policies", new
        {
            expectedCatalogVersion = 1,
            policy = new { mode = 0, expiryRequired = true, sellThroughExpiryDate = true, warningDays = 7, overrideAllowed = false },
            reason = "Require source dates",
        });
        Assert.True(policy.IsSuccessStatusCode, await policy.Content.ReadAsStringAsync());
        var held = await RecoverAsync(shop, sale);
        await CorrectDateAsync(shop, held.Batch);
        return (sale, held.Root, (await BatchAsync(sale)).Version);
    }

    private async Task GrantAsync(Shop shop, DateTimeOffset end, DateTimeOffset? verified = null,
        CustomerSuspensionV1? security = null, bool accountSecurity = false,
        string state = "Active", bool enforced = true)
    {
        var shopId = ShopId(shop);
        var account = Guid.NewGuid();
        var request = Guid.NewGuid();
        var term = Guid.NewGuid();
        await using var migrator = Open(Factory.MigratorConnectionString);
        var membership = await migrator.ShopMemberships.SingleAsync(x => x.ShopId == shopId && x.Role == ShopRole.Owner);
        var now = _clock.Now;
        foreach (var kind in new[] { "Granted", "Accepted" })
            await migrator.Database.ExecuteSqlAsync($"""
                INSERT INTO public.shop_affiliation_consent_events (id, shop_id, account_id, request_id,
                    actor_id, membership_id, authority_version, operation_key, kind, recorded_at, term_id,
                    shop_name, owner_name, request_expires_at)
                VALUES ({Guid.NewGuid()}, {shopId}, {account}, {request},
                    {membership.UserId}, {membership.Id}, {membership.AuthorityVersion}, {Guid.NewGuid()}, {kind}, {now},
                    {term}, {"Fixture Shop"}, {"Fixture Owner"}, {now.AddDays(15)})
                """);
        await using var db = Open(Factory.RuntimeConnectionString);
        var proofTime = verified ?? now;
        var snapshot = new EntitlementSnapshotV1(1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
            account, shopId, request, new(1, proofTime, accountSecurity ? "Security" : "Unrestricted", null, accountSecurity ? security : null),
            new(1, proofTime, state, term, "Paid", proofTime.AddDays(-1), end, end, 2, 3,
                accountSecurity ? null : security));
        var body = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        var proof = new VerifiedServiceAssertion(Guid.NewGuid(), "test-key", "test-public-key", now, now.AddMinutes(1),
            SignedServiceAssertion.Digest(body), "fixture-verified-assertion");
        var fence = new ShopLicenceFence(db, new LicenceActionPolicy(), _clock);
        var result = await new EntitlementProjectionStore(db, _clock, fence).ApplyAsync(snapshot, body, proof, CancellationToken.None);
        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        Assert.Equal("Applied", JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(result.Value, EntitlementSnapshotsV1.Json)?.Outcome);
        if (enforced)
            Assert.Equal(1, await migrator.Database.ExecuteSqlAsync($"""
                UPDATE public.shop_license_states SET enforcement_activated_at = {now} WHERE shop_id = {shopId}
                """));
    }

    private static Guid ShopId(Shop shop) =>
        Guid.Parse(new JwtSecurityToken(shop.Token).Claims.Single(x => x.Type == "active_shop_id").Value);

    private static ApplicationDbContext Open(string connection) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private long _ticks = now.UtcTicks;
        public DateTimeOffset Now
        {
            get => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
            set => Interlocked.Exchange(ref _ticks, value.UtcTicks);
        }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
