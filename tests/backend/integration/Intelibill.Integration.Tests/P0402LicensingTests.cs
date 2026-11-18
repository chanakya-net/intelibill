using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static Intelibill.Application.Common.Licensing.LicenceOutcome;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed partial class P0402LicensingTests(PostgreSqlTestFixture fixture)
{
    private static readonly DateTimeOffset VerifiedAt = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private readonly TestClock _clock = new(VerifiedAt.AddMinutes(1));
    private static CancellationToken Ct => CancellationToken.None;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cached_authority_stops_at_the_first_verified_boundary(bool accountOlder)
    {
        var binding = await SeedAsync();
        await using var db = CreateDb();
        var later = VerifiedAt.AddMinutes(1);
        await ApplyAsync(db, binding, accountOlder ? VerifiedAt : later, accountOlder ? later : VerifiedAt,
            VerifiedAt.AddDays(10));

        var cap = VerifiedAt.AddHours(48);
        await AssertTradingAsync(binding, cap.AddMicroseconds(-1), Allowed);
        await AssertTradingAsync(binding, cap, Blocked, "Licence.VerificationUnavailable");
        await AssertTradingAsync(binding, cap.AddMicroseconds(1), Blocked, "Licence.VerificationUnavailable");
    }

    private async Task AssertTradingAsync(Binding binding, DateTimeOffset now, LicenceOutcome outcome, string? code = null,
        LicenceAction action = LicenceAction.NewTrading)
    {
        _clock.Now = now;
        await using var db = CreateDb();
        await using var transaction = await BeginScopedAsync(db, binding.ShopId);
        var result = await EnterAsync(db, binding.ShopId, action);
        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        Assert.Equal(outcome, result.Value.Decision.Outcome);
        Assert.Equal(code, result.Value.Decision.Denial?.Code);
    }

    private Task<ErrorOr<ShopLicenceLease>> EnterAsync(ApplicationDbContext db, Guid shop,
        LicenceAction action = LicenceAction.NewTrading) =>
        new ShopLicenceFence(db, new LicenceActionPolicy(), _clock).EnterAsync(shop, action, Ct);

    private async Task ApplyAsync(ApplicationDbContext db, Binding binding, DateTimeOffset accountVerified, DateTimeOffset shopVerified,
        DateTimeOffset end, long shopRevision = 1, string restriction = "Unrestricted", long accountRevision = 1, DateTimeOffset? boundary = null,
        string state = "Active")
    {
        var now = _clock.GetUtcNow();
        var snapshot = new EntitlementSnapshotV1(1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
            binding.AccountId, binding.ShopId, binding.RequestId, new(accountRevision, accountVerified, restriction, null),
            new(shopRevision, shopVerified, state, binding.TermId, "Trial", VerifiedAt.AddDays(-1), end, boundary ?? end, 2, 3));
        var body = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        var proof = new VerifiedServiceAssertion(Guid.NewGuid(), "test-key", "test-public-key", now, now.AddMinutes(1),
            SignedServiceAssertion.Digest(body), "fixture-verified-assertion");
        var fence = new ShopLicenceFence(db, new LicenceActionPolicy(), _clock);
        var result = await new EntitlementProjectionStore(db, _clock, fence).ApplyAsync(snapshot, body, proof, Ct);
        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        var receipt = JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(result.Value, EntitlementSnapshotsV1.Json);
        Assert.Equal("Applied", receipt?.Outcome);
    }

    private async Task<Binding> SeedAsync(Guid? account = null)
    {
        await using var db = CreateDb(owner: true);
        var shop = Shop.Create("P04 Test Shop", "42 MG Road", "Bengaluru", "Karnataka", "560001", null, null, null);
        var owner = User.CreateWithEmail($"p0402-{Guid.NewGuid():N}@fixture.test", "fixture", "Fixture", "Owner");
        var membership = ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, isDefault: true);
        db.AddRange(shop, owner, membership);
        await db.SaveChangesAsync(Ct);

        return await BindAsync(db, membership, shop.Name, account);
    }

    private async Task<Binding> BindAsync(ApplicationDbContext db, ShopMembership membership, string shopName, Guid? account = null)
    {
        var binding = new Binding(membership.ShopId, account ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var now = _clock.GetUtcNow();
        foreach (var kind in new[] { "Granted", "Accepted" })
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO public.shop_affiliation_consent_events (id, shop_id, account_id, request_id,
                    actor_id, membership_id, authority_version, operation_key, kind, recorded_at, term_id,
                    shop_name, owner_name, request_expires_at)
                VALUES ({Guid.NewGuid()}, {binding.ShopId}, {binding.AccountId}, {binding.RequestId},
                    {membership.UserId}, {membership.Id}, {membership.AuthorityVersion}, {Guid.NewGuid()}, {kind}, {now},
                    {binding.TermId}, {shopName}, {"Fixture Owner"}, {now.AddDays(15)})
                """, Ct);
        return binding;
    }

    private string MigratorConnection => fixture.MigratorConnectionString;

    private ApplicationDbContext CreateDb(bool owner = false) =>
        Open(owner ? MigratorConnection : RestrictedRuntimeRole.ConnectionStringFor(MigratorConnection));

    private static ApplicationDbContext Open(string connection) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);

    private static async Task<IDbContextTransaction> BeginScopedAsync(ApplicationDbContext db, Guid shop)
    {
        var tx = await db.Database.BeginTransactionAsync(Ct);
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {shop.ToString("D")}, true)", Ct);
        return tx;
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record Binding(Guid ShopId, Guid AccountId, Guid RequestId, Guid TermId);
}
