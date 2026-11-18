using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;
using static Intelibill.Application.Common.Licensing.LicenceOutcome;

namespace Intelibill.Integration.Tests;

public sealed partial class P0402LicensingTests
{
    private const string Unavailable = "Licence.VerificationUnavailable";

    [Fact]
    public async Task Future_dated_proof_never_ripens_into_trading_authority()
    {
        _clock.Now = EntitlementSnapshotsV1.Instant(TimeProvider.System);
        using var signer = RSA.Create(2048);
        var transport = new FixtureTransport();
        var host = MerchantHost(signer, transport);
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var (shop, token) = await EnrollShopAsync(client);
        var binding = await BindShopAsync(shop);
        var receivedAt = _clock.Now;
        var stamped = receivedAt.AddDays(1);
        var body = SnapshotBody(binding, stamped, receivedAt.AddDays(10));
        var receipt = await PostSnapshotAsync(client, signer, body);

        await AssertTradingAsync(binding, receivedAt, Blocked, Unavailable);
        await AssertTradingAsync(binding, stamped, Blocked, Unavailable);

        await host.DisposeAsync();
        using var restarted = MerchantHost(signer, transport);
        using var afterRestart = CreateClient(restarted);
        afterRestart.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(receipt, await PostSnapshotAsync(afterRestart, signer, body));
        await AssertTradingAsync(binding, stamped.AddMinutes(1), Blocked, Unavailable);

        var coherent = stamped.AddMinutes(2);
        _clock.Now = coherent;
        await PostSnapshotAsync(afterRestart, signer, SnapshotBody(binding, coherent, receivedAt.AddDays(10), revision: 2));
        await AssertTradingAsync(binding, coherent, Allowed);
    }

    [Theory]
    [InlineData(999_999, true)]
    [InlineData(1_000_000, false)]
    public async Task Proof_may_only_be_stamped_within_its_signing_second(int microseconds, bool coherent)
    {
        var binding = await SeedAsync();
        var signedAt = _clock.Now;
        var stamped = signedAt.AddMicroseconds(microseconds);
        await using (var db = CreateDb())
            await ApplyAsync(db, binding, stamped, stamped, VerifiedAt.AddDays(10));

        var readAt = signedAt.AddSeconds(2);
        if (coherent)
            await AssertTradingAsync(binding, readAt, Allowed);
        else
            await AssertTradingAsync(binding, readAt, Blocked, Unavailable);
    }

    [Fact]
    public async Task Sibling_account_proof_is_judged_without_exposing_it_to_the_shop()
    {
        var shop = await SeedAsync();
        var sibling = await SeedAsync(shop.AccountId);
        var end = VerifiedAt.AddDays(10);
        await using (var db = CreateDb())
            await ApplyAsync(db, shop, VerifiedAt, VerifiedAt, end);

        var later = VerifiedAt.AddHours(1);
        _clock.Now = later;
        await using (var db = CreateDb())
            await ApplyAsync(db, sibling, later, later, end, accountRevision: 2);
        await AssertTradingAsync(shop, later, Allowed);
        await AssertEvidenceHiddenAsync(shop, sibling);

        var futureAccount = later.AddDays(1);
        await using (var db = CreateDb())
            await ApplyAsync(db, sibling, futureAccount, later, end, shopRevision: 2, accountRevision: 3);
        await AssertTradingAsync(shop, futureAccount.AddMinutes(1), Blocked, Unavailable);

        _clock.Now = later;
        await using (var db = CreateDb())
            await ApplyAsync(db, sibling, futureAccount.AddMinutes(1), later, end, 3, "Security", 4);
        await AssertTradingAsync(shop, later, Blocked, "Licence.SecuritySuspended");
    }

    [Fact]
    public async Task Proof_issuance_reaches_sibling_evidence_under_a_schema_owner_without_superuser_rights()
    {
        var shop = await SeedAsync();
        var sibling = await SeedAsync(shop.AccountId);
        await using (var db = CreateDb())
            await ApplyAsync(db, shop, VerifiedAt, VerifiedAt, VerifiedAt.AddDays(10));
        await using (var db = CreateDb())
            await ApplyAsync(db, sibling, _clock.Now, _clock.Now, VerifiedAt.AddDays(10), accountRevision: 2);

        await using var owner = CreateDb(owner: true);
        await using var transaction = await owner.Database.BeginTransactionAsync(Ct);
        await owner.Database.ExecuteSqlRawAsync(TransferLicensingOwnership, Ct);
        await owner.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {shop.ShopId.ToString("D")}, true)", Ct);
        await owner.Database.ExecuteSqlRawAsync("SET LOCAL ROLE intelibill_app", Ct);

        var issued = await owner.Database.SqlQuery<Domain.Policies.LicenseProofIssuance>(
            $"SELECT * FROM public.merchant_licence_proof_issuance({shop.ShopId})").ToListAsync(Ct);
        var proof = Assert.Single(issued);
        Assert.Equal(_clock.Now, proof.ShopIssuedAt);
        Assert.Equal(_clock.Now, proof.AccountIssuedAt);
        Assert.Equal(0, await owner.Set<LicenseGrantEvidence>().CountAsync(row => row.ShopId == sibling.ShopId, Ct));
        await transaction.RollbackAsync(Ct);
    }

    // Re-owns the objects as a plain role, rewriting only the owner named by each migrated policy.
    private const string TransferLicensingOwnership = """
        CREATE ROLE p0402_schema_owner NOLOGIN NOSUPERUSER NOBYPASSRLS;
        DO $$ DECLARE policy record; BEGIN
            FOR policy IN SELECT p.polname, p.polrelid::regclass AS relation, pg_get_expr(p.polqual, p.polrelid) AS rule
                FROM pg_policy p WHERE p.polrelid = ANY (ARRAY['public.license_grant_evidence',
                    'public.shop_license_states', 'public.account_license_restrictions']::regclass[]) LOOP
                EXECUTE format('ALTER POLICY %I ON %s USING (%s)', policy.polname, policy.relation,
                    replace(policy.rule, quote_literal(current_user), quote_literal('p0402_schema_owner')));
                EXECUTE format('ALTER TABLE %s OWNER TO p0402_schema_owner', policy.relation);
            END LOOP;
        END $$;
        ALTER FUNCTION public.merchant_licence_proof_issuance(uuid) OWNER TO p0402_schema_owner;
        """;

    private async Task AssertEvidenceHiddenAsync(Binding shop, Binding sibling)
    {
        await using var db = CreateDb();
        await using var transaction = await BeginScopedAsync(db, shop.ShopId);
        Assert.Equal(0, await db.Set<LicenseGrantEvidence>().CountAsync(row => row.ShopId == sibling.ShopId, Ct));
    }

    [Theory]
    [InlineData("PendingActivation", false)]
    [InlineData("Scheduled", true)]
    public async Task Waiting_state_becomes_an_outage_at_the_authority_cutoff(string state, bool laterBoundary)
    {
        _clock.Now = EntitlementSnapshotsV1.Instant(TimeProvider.System);
        using var signer = RSA.Create(2048);
        using var host = MerchantHost(signer, new FixtureTransport());
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var (shop, _) = await EnrollShopAsync(client);
        var binding = await BindShopAsync(shop);
        var verified = _clock.Now.AddHours(-47).AddMinutes(-59);
        DateTimeOffset? boundary = laterBoundary ? verified.AddDays(10) : null;
        await PostSnapshotAsync(client, signer, WaitingSnapshotBody(binding, verified, state, boundary));

        var cutoff = verified.AddHours(48);
        _clock.Now = cutoff.AddMicroseconds(-1);
        await AssertAuthorityCodeAsync(client, shop, HttpStatusCode.Forbidden, "Licence.PendingActivation");
        _clock.Now = cutoff;
        await AssertAuthorityCodeAsync(client, shop, HttpStatusCode.ServiceUnavailable, Unavailable);
    }

    private static async Task AssertAuthorityCodeAsync(HttpClient client, Guid shop, HttpStatusCode status, string code)
    {
        using var response = await client.GetAsync($"/api/licensing/shops/{shop:D}/trading-authority", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(status == response.StatusCode, text);
        using var body = JsonDocument.Parse(text);
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Predecessor_schedule_cannot_delay_the_upgraded_scan(bool overwritesExistingRequest)
    {
        using var key = RSA.Create(2048);
        using var host = MerchantHost(key, new());
        await host.InitializeAsync();
        await GrantDeliveryWorkerAsync();
        var warm = await SeedAsync();
        await using (var projection = CreateDb())
            await ApplyAsync(projection, warm, VerifiedAt, VerifiedAt, VerifiedAt.AddDays(10));
        await using var owner = CreateDb(owner: true);
        if (overwritesExistingRequest)
        {
            await ScanAsync(host);
            await MarkRequestsDeliveredAsync(owner);
            _clock.Now = _clock.Now.AddSeconds(56);
        }

        var queuedAt = _clock.Now;
        await QueueAsPredecessorAsync(owner, warm, queuedAt);
        var requests = await RequestCountAsync(owner, warm.ShopId);

        _clock.Now = queuedAt.AddMinutes(1);
        await ScanAsync(host);
        Assert.Equal(requests, await RequestCountAsync(owner, warm.ShopId)); // Still outstanding: coalesced.

        await MarkRequestsDeliveredAsync(owner);
        await ScanAsync(host);
        await ScanAsync(host);
        Assert.Equal(requests + 1, await RequestCountAsync(owner, warm.ShopId));
    }

    /// <summary>The statement the previous release runs, unchanged, against the migrated schema.</summary>
    private static async Task QueueAsPredecessorAsync(ApplicationDbContext db, Binding binding, DateTimeOffset now)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        var request = new EntitlementReconciliationV1(1, Guid.NewGuid(), binding.AccountId, binding.ShopId,
            binding.RequestId, 1, 1);
        var body = JsonSerializer.SerializeToUtf8Bytes(request, EntitlementSnapshotsV1.Json);
        await new LeasedHttpOutbox(db).AppendAsync(request.EventId, binding.AccountId, binding.ShopId, "Reconciliation",
            body, SignedServiceAssertion.Digest(body), now, Ct);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.entitlement_reconciliation_requests (shop_id, event_id, next_check_at)
            VALUES ({binding.ShopId}, {request.EventId}, {now.AddHours(1)})
            ON CONFLICT (shop_id) DO UPDATE SET event_id = EXCLUDED.event_id, next_check_at = EXCLUDED.next_check_at
            """, Ct);
        await transaction.CommitAsync(Ct);
    }

    [Fact]
    public async Task New_shop_awaiting_its_first_grant_keeps_owner_setup_only()
    {
        var binding = await SeedAsync();
        await AssertTradingAsync(binding, _clock.Now, RequiresOwnerSetup, action: LicenceAction.BasicShopSetup);
        await AssertTradingAsync(binding, _clock.Now, Blocked, Unavailable);
        await AssertTradingAsync(binding, _clock.Now, Blocked, Unavailable, LicenceAction.OperationalCapacity);

        var restricted = new AccountLicenseRestriction();
        restricted.Apply(1, VerifiedAt, "Security", null, Guid.NewGuid());
        var decision = new LicenceActionPolicy().Decide(LicenceAction.BasicShopSetup, null, restricted, null, _clock.Now);
        Assert.Equal(Blocked, decision.Outcome);
        Assert.Equal("Licence.SecuritySuspended", decision.Denial?.Code);

        await using (var db = CreateDb())
            await ApplyAsync(db, binding, VerifiedAt, VerifiedAt, VerifiedAt.AddDays(10), restriction: "Security",
                state: "PendingActivation");
        await AssertTradingAsync(binding, _clock.Now, Blocked, "Licence.SecuritySuspended", LicenceAction.BasicShopSetup);
    }
}
