using System.Data;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Infrastructure.Repositories;
using Intelibill.Infrastructure.Services.Posting;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static Intelibill.Application.Common.Licensing.LicenceOutcome;

namespace Intelibill.Integration.Tests;

public sealed class P0401LicensingTests
{
    private static readonly DateTimeOffset Deadline = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<LicenceAction, LicenceOutcome, LicenceOutcome, LicenceOutcome, LicenceOutcome> Matrix => new()
    {
        { LicenceAction.Health,              Allowed,            Allowed, Allowed,              Allowed },
        { LicenceAction.Authentication,      Allowed,            Allowed, Allowed,              Allowed },
        { LicenceAction.Billing,             Allowed,            Allowed, Allowed,              Allowed },
        { LicenceAction.BasicShopSetup,       RequiresOwnerSetup, Allowed, Blocked,              Blocked },
        { LicenceAction.NewTrading,           Blocked,            Allowed, Blocked,              Blocked },
        { LicenceAction.OperationalCapacity,  Blocked,            Allowed, Blocked,              Blocked },
        { LicenceAction.History,              Allowed,            Allowed, Allowed,              Blocked },
        { LicenceAction.TillCompletion,       RequiresProvenSource, Allowed, RequiresProvenSource, Blocked },
        { LicenceAction.DueSettlement,        RequiresProvenSource, Allowed, RequiresProvenSource, Blocked },
        { LicenceAction.ExistingPayment,      RequiresProvenSource, Allowed, RequiresProvenSource, Blocked },
        { LicenceAction.SourceCorrection,     RequiresProvenSource, Allowed, RequiresProvenSource, Blocked },
        { LicenceAction.GeneralStock,         Blocked,            Allowed, Blocked,              Blocked },
        { LicenceAction.SafetyDisposition,    RequiresProvenSource, Allowed, RequiresProvenSource, Blocked },
        { LicenceAction.Recovery,             RequiresProvenSource, Allowed, RequiresProvenSource, Blocked },
        { LicenceAction.MemberRemoval,        Allowed,            Allowed, Allowed,              Allowed },
        { LicenceAction.InternalService,      Allowed,            Allowed, Allowed,              Allowed },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Matrix_matches_each_parent_row(LicenceAction action, LicenceOutcome pending, LicenceOutcome operating,
        LicenceOutcome restricted, LicenceOutcome security)
    {
        var policy = new LicenceActionPolicy();
        var clock = new TestClock(Deadline.AddTicks(-1));
        var unrestricted = Account("Unrestricted");
        var trial = ShopState("Active", "Trial");
        var active = ShopState("Active", "Paid");
        var grace = ShopState("Grace", "Paid");

        Assert.Equal(pending, policy.Decide(action, ShopState("PendingActivation"), unrestricted, clock.GetUtcNow()).Outcome);
        Assert.Equal(pending, policy.Decide(action, ShopState("Scheduled"), unrestricted, clock.GetUtcNow()).Outcome);
        Assert.Equal(operating, policy.Decide(action, trial, unrestricted, clock.GetUtcNow()).Outcome);
        Assert.Equal(operating, policy.Decide(action, active, unrestricted, clock.GetUtcNow()).Outcome);
        Assert.Equal(operating, policy.Decide(action, grace, unrestricted, clock.GetUtcNow()).Outcome);
        Assert.Equal(restricted, policy.Decide(action, active, Account("Trading"), clock.GetUtcNow()).Outcome);
        foreach (var state in new[] { "Expired", "Suspended", "Closed" })
            Assert.Equal(restricted, policy.Decide(action, ShopState(state), unrestricted, clock.GetUtcNow()).Outcome);
        foreach (var shop in new[] { ShopState("PendingActivation"), active, ShopState("Expired") })
            Assert.Equal(security, policy.Decide(action, shop, Account("Security"), clock.GetUtcNow()).Outcome);

        clock.Now = Deadline;
        Assert.Equal(restricted, policy.Decide(action, trial, unrestricted, clock.GetUtcNow()).Outcome);
        Assert.Equal(restricted, policy.Decide(action, active, unrestricted, clock.GetUtcNow()).Outcome);
        Assert.Equal(restricted, policy.Decide(action, grace, unrestricted, clock.GetUtcNow()).Outcome);
    }

    [Fact]
    public void Matrix_covers_every_action_and_requires_both_projection_scopes_for_trading()
    {
        Assert.Equal(Enum.GetValues<LicenceAction>(), Matrix.Select(row => (LicenceAction)row[0]).Order());
        var policy = new LicenceActionPolicy();
        var early = Deadline.AddMinutes(-1);
        var active = ShopState("Active", "Paid");
        Assert.Equal(Blocked, policy.Decide(LicenceAction.NewTrading, active, null, early).Outcome);
        Assert.Equal(Blocked, policy.Decide(LicenceAction.NewTrading, null, Account("Unrestricted"), early).Outcome);
        Assert.Equal(Blocked, policy.Decide(LicenceAction.NewTrading, ShopState("EvidenceIncomplete"), Account("Unrestricted"), early).Outcome);
        Assert.Equal(Blocked, policy.Decide(LicenceAction.NewTrading, ShopState("NeedsReview"), Account("Unrestricted"), early).Outcome);
    }

    private static ShopLicenseState ShopState(string state, string? basis = null)
    {
        var shop = new ShopLicenseState();
        shop.Apply(1, Deadline.AddMinutes(-1), state, Guid.NewGuid(), basis,
            Deadline.AddDays(-1), Deadline, Deadline, 1, 3, Guid.NewGuid());
        return shop;
    }

    private static AccountLicenseRestriction Account(string restriction)
    {
        var account = new AccountLicenseRestriction();
        account.Apply(1, Deadline.AddMinutes(-1), restriction, null, Guid.NewGuid());
        return account;
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Collection("Integration Tests")]
    public sealed class PostgreSql(PostgreSqlTestFixture fixture)
    {
        private readonly TestClock _clock = new(Deadline.AddMinutes(-1));
        private static CancellationToken Ct => CancellationToken.None;

        [Fact]
        public async Task Fence_requires_a_read_committed_source_transaction()
        {
            await using var db = CreateDb();
            var shop = Guid.NewGuid();
            await Assert.ThrowsAsync<InvalidOperationException>(() => EnterAsync(db, shop));
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, Ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => EnterAsync(db, shop));
        }

        [Fact]
        public async Task Fence_is_observe_only_until_enrolled_and_refuses_a_foreign_scope()
        {
            var binding = await SeedAsync();
            await using var db = CreateDb();
            await using var tx = await BeginScopedAsync(db, binding.ShopId);
            var empty = await EnterAsync(db, binding.ShopId);
            Assert.False(empty.Value.Decision.Enforced);
            Assert.Equal(Blocked, empty.Value.Decision.Outcome);
            var mismatch = await EnterAsync(db, Guid.NewGuid());
            Assert.Equal("Licence.ScopeMismatch", mismatch.FirstError.Code);
            await tx.CommitAsync(Ct);

            await using var projection = CreateDb();
            await ApplyAsync(projection, binding, 1, "Unrestricted");
            _clock.Now = Deadline;
            await AssertExpiryAsync(db, binding.ShopId, enforced: false);
            await using var owner = CreateDb(owner: true);
            await owner.Database.ExecuteSqlAsync($"""
                UPDATE public.shop_license_states SET enforcement_activated_at = {Deadline.AddMinutes(-1)}
                WHERE shop_id = {binding.ShopId}
                """, Ct);
            await AssertExpiryAsync(db, binding.ShopId, enforced: true);
        }

        [Fact]
        public async Task Fence_timeout_recovers_the_transaction_and_restores_the_callers_lock_timeout()
        {
            var binding = await SeedAsync();
            await using var projection = CreateDb();
            await ApplyAsync(projection, binding, 1, "Unrestricted");
            await using var holder = CreateDb(owner: true);
            await using var held = await holder.Database.BeginTransactionAsync(Ct);
            await holder.Database.ExecuteSqlAsync($"""
                SELECT pg_advisory_xact_lock(hashtextextended({'s' + binding.ShopId.ToString("D")}, 859))
                """, Ct);

            await using var db = CreateDb();
            await using var tx = await BeginScopedAsync(db, binding.ShopId);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '173ms'", Ct);
            var sourcePid = await PidAsync(db);
            var holderPid = await PidAsync(holder);
            var entering = EnterAsync(db, binding.ShopId);
            try
            {
                await WaitForBlockAsync(entering, sourcePid, holderPid, "Source must wait for the held shop lock.");
                Assert.False(await TryAccountLockAsync(holder, binding.AccountId),
                    "Source must hold the account lock before waiting for the shop lock.");
            }
            finally
            {
                await entering.WaitAsync(TimeSpan.FromSeconds(8));
            }

            var error = await entering;
            Assert.True(error.IsError, "The shop is held exclusively, so the source must return Licence.Busy.");
            Assert.Equal(ErrorType.Conflict, error.FirstError.Type);
            Assert.Equal("Licence.Busy", error.FirstError.Code);
            Assert.Equal("173ms", await LockTimeoutAsync(db));
            Assert.True(await TryAccountLockAsync(holder, binding.AccountId),
                "The timed-out attempt must release its partial account lock.");
            await held.RollbackAsync(Ct);

            Assert.Equal(Allowed, (await EnterAsync(db, binding.ShopId)).Value.Decision.Outcome);
            Assert.Equal("173ms", await LockTimeoutAsync(db));
            await tx.CommitAsync(Ct);
        }

        [Fact]
        public async Task Fence_precedes_the_period_and_projection_waits_for_the_source_commit()
        {
            var binding = await SeedAsync();
            await using var delivery = CreateDb();
            await ApplyAsync(delivery, binding, 1, "Unrestricted");
            var date = new DateOnly(2026, 11, 1);
            await using var periodDb = CreateDb();
            await using var close = await BeginScopedAsync(periodDb, binding.ShopId);
            var periods = new PostingPeriodRepository(periodDb);
            await periods.EnsureExistsAsync(binding.ShopId, date, date.AddMonths(1), Ct);
            await periodDb.Database.CommitTransactionAsync(Ct);
            await using var closing = await BeginScopedAsync(periodDb, binding.ShopId);
            await periodDb.Database.ExecuteSqlAsync($"""
                SELECT id FROM public.posting_periods WHERE shop_id = {binding.ShopId} AND start_date = {date} FOR UPDATE
                """, Ct);

            await using var source = CreateDb();
            await using var post = await BeginScopedAsync(source, binding.ShopId);
            var sourcePid = await PidAsync(source);
            var initial = await EnterAsync(source, binding.ShopId);
            Assert.Equal(Allowed, initial.Value.Decision.Outcome);
            var periodPolicy = new PostingPeriodPolicy(source, new PostingPeriodRepository(source));
            var posting = periodPolicy.AuthorizeForWriteAsync(binding.ShopId, date, Ct);
            Task? restriction = null;
            try
            {
                await WaitForBlockAsync(posting, sourcePid, await PidAsync(periodDb), "Source must wait for the period closer.");
                await delivery.Database.OpenConnectionAsync(Ct);
                var deliveryPid = await PidAsync(delivery);
                restriction = ApplyAsync(delivery, binding, 2, "Trading");
                await WaitForBlockAsync(restriction, deliveryPid, sourcePid,
                    "Projection must wait for the source licence fence.");
                await closing.RollbackAsync(Ct);
                Assert.False((await posting.WaitAsync(TimeSpan.FromSeconds(6))).IsError);
                Assert.False(restriction.IsCompleted, "Taking the period fence must preserve the source licence fence.");
            }
            finally
            {
                if (periodDb.Database.CurrentTransaction is not null)
                    await closing.RollbackAsync(Ct);
                await posting.WaitAsync(TimeSpan.FromSeconds(6));
                await post.CommitAsync(Ct);
                if (restriction is not null)
                    await restriction.WaitAsync(TimeSpan.FromSeconds(6));
            }

            await using var next = await BeginScopedAsync(source, binding.ShopId);
            var applied = (await EnterAsync(source, binding.ShopId)).Value;
            Assert.Equal(2L, applied.AccountRevision);
            Assert.Equal(Blocked, applied.Decision.Outcome);
            Assert.Equal("Licence.TradingRestricted", applied.Decision.Denial?.Code);
        }

        [Fact]
        public async Task Source_waits_for_projection_on_a_sibling_shop_and_reads_the_new_account_restriction()
        {
            var first = await SeedAsync();
            var sibling = await SeedAsync(first.AccountId);
            await using var delivery = CreateDb();
            await ApplyAsync(delivery, first, 1, "Unrestricted");
            await ApplyAsync(delivery, sibling, 1, "Unrestricted");
            await delivery.Database.OpenConnectionAsync(Ct);
            var deliveryPid = await PidAsync(delivery);

            await using var holder = CreateDb(owner: true);
            await using var pending = await holder.Database.BeginTransactionAsync(Ct);
            await holder.Database.ExecuteSqlRawAsync("LOCK TABLE public.entitlement_request_uses IN ACCESS EXCLUSIVE MODE", Ct);
            var restriction = ApplyAsync(delivery, first, 2, "Security");
            await using var source = CreateDb();
            await using var tx = await BeginScopedAsync(source, sibling.ShopId);
            Task<ErrorOr<ShopLicenceLease>>? reading = null;
            try
            {
                await WaitForBlockAsync(restriction, deliveryPid, await PidAsync(holder), "Projection must hold the licence locks while applying.");
                var sourcePid = await PidAsync(source);
                reading = EnterAsync(source, sibling.ShopId, LicenceAction.History);
                await WaitForBlockAsync(reading, sourcePid, deliveryPid, "Sibling must wait for its shared account restriction.");
            }
            finally
            {
                await pending.RollbackAsync(Ct);
                await restriction.WaitAsync(TimeSpan.FromSeconds(6));
                if (reading is not null)
                    await reading.WaitAsync(TimeSpan.FromSeconds(6));
            }

            var result = (await reading.WaitAsync(TimeSpan.FromSeconds(6))).Value;
            Assert.Equal(2L, result.AccountRevision);
            Assert.Equal(1L, result.ShopRevision);
            Assert.Equal(Blocked, result.Decision.Outcome);
            Assert.Equal("Licence.SecuritySuspended", result.Decision.Denial?.Code);

        }

        private async Task AssertExpiryAsync(ApplicationDbContext db, Guid shop, bool enforced)
        {
            await using var tx = await BeginScopedAsync(db, shop);
            var decision = (await EnterAsync(db, shop)).Value.Decision;
            Assert.Equal(enforced, decision.Enforced);
            Assert.Equal(Blocked, decision.Outcome);
            Assert.Equal("Licence.Expired", decision.Denial?.Code);
        }

        private Task<ErrorOr<ShopLicenceLease>> EnterAsync(ApplicationDbContext db, Guid shop,
            LicenceAction action = LicenceAction.NewTrading) =>
            new ShopLicenceFence(db, new LicenceActionPolicy(), _clock).EnterAsync(shop, action, Ct);

        private async Task ApplyAsync(ApplicationDbContext db, Binding binding, long revision, string restriction)
        {
            var now = _clock.GetUtcNow();
            var snapshot = new EntitlementSnapshotV1(1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
                binding.AccountId, binding.ShopId, binding.RequestId, new(revision, now, restriction, null),
                new(revision, now, "Active", binding.TermId, "Trial", Deadline.AddDays(-1), Deadline, Deadline, 2, 3));
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
            var owner = User.CreateWithEmail($"p0401-{Guid.NewGuid():N}@fixture.test", "fixture", "Fixture", "Owner");
            var membership = ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, isDefault: true);
            db.AddRange(shop, owner, membership);
            await db.SaveChangesAsync(Ct);

            var binding = new Binding(shop.Id, account ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var now = _clock.GetUtcNow();
            foreach (var kind in new[] { "Granted", "Accepted" })
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO public.shop_affiliation_consent_events (id, shop_id, account_id, request_id,
                        actor_id, membership_id, authority_version, operation_key, kind, recorded_at, term_id,
                        shop_name, owner_name, request_expires_at)
                    VALUES ({Guid.NewGuid()}, {binding.ShopId}, {binding.AccountId}, {binding.RequestId},
                        {owner.Id}, {membership.Id}, {membership.AuthorityVersion}, {Guid.NewGuid()}, {kind}, {now},
                        {binding.TermId}, {shop.Name}, {"Fixture Owner"}, {Deadline})
                    """, Ct);
            return binding;
        }

        private ApplicationDbContext CreateDb(bool owner = false) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(owner ? fixture.MigratorConnectionString : fixture.RuntimeConnectionString)
            .UseSnakeCaseNamingConvention().Options);

        private static async Task<IDbContextTransaction> BeginScopedAsync(ApplicationDbContext db, Guid shop)
        {
            var tx = await db.Database.BeginTransactionAsync(Ct);
            await db.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {shop.ToString("D")}, true)", Ct);
            return tx;
        }

        private async Task WaitForBlockAsync(Task operation, int waiter, int blocker, string message)
        {
            await using var observer = CreateDb(owner: true);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (!await observer.Database.SqlQuery<bool>($"""
                SELECT {blocker} = ANY(pg_blocking_pids({waiter})) AS "Value"
                """).SingleAsync(deadline.Token))
            {
                Assert.False(operation.IsCompleted, message);
                await Task.Delay(20, deadline.Token);
            }
        }

        private static Task<bool> TryAccountLockAsync(ApplicationDbContext db, Guid account) =>
            db.Database.SqlQuery<bool>($"""
                SELECT pg_try_advisory_xact_lock(hashtextextended({'a' + account.ToString("D")}, 859)) AS "Value"
                """).SingleAsync(Ct);

        private static Task<int> PidAsync(ApplicationDbContext db) =>
            db.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Ct);

        private static Task<string> LockTimeoutAsync(ApplicationDbContext db) =>
            db.Database.SqlQuery<string>($"SELECT current_setting('lock_timeout') AS \"Value\"").SingleAsync(Ct);

        private sealed record Binding(Guid ShopId, Guid AccountId, Guid RequestId, Guid TermId);
    }
}
