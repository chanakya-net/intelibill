using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.LicensingPostgreSqlFixture;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0307Tests
{
    [Fact]
    public async Task UpgradeRetainsEvidenceAndReviewCannotBypassGuards()
    {
        const string previous = "20261117150000_GatewayInboxEvents";
        var now = P0210Fixture.Start;
        await using var fixture = await StartAsync(database, new P0201Clock { Now = now }, P0306Tests.Settings, services: services => { P0310LegacyRouting.InstallLegacy(services); P0311LegacyIssuance.InstallLegacy(services); });
        var purchase = await fixture.AcceptedQuoteAsync(1);
        using var finance = await P0306Tests.SignInBillingAsync(fixture);
        var path = P0306Tests.Path(purchase.Quote);
        using var paid = await P0212Fixture.PostAsync(finance, path, P0306Tests.Receipt(purchase.Quote, now));
        Assert.Equal(HttpStatusCode.Created, paid.StatusCode);

        var online = await fixture.AcceptedQuoteAsync(1);
        var start = await StartAsync(online.Payer.Client, online.Quote, HttpStatusCode.Created);
        var attempt = start.GetProperty("attemptId").GetGuid();

        await using var context = fixture.Hosts.Fixture.OpenMigrationContext();
        var migrations = context.GetService<IMigrator>();
        await migrations.MigrateAsync(previous);
        var owner = fixture.Hosts.LicensingOwner;
        Assert.Equal(false, await ReadScalarAsync(owner, "SELECT to_regclass('public.unmatched_payments') IS NOT NULL"));
        await SeedLegacyDuplicateAsync(owner);
        await SeedLegacyClockSkewAsync(owner, online.Account, attempt);
        var original = await ManualBytesAsync(owner);
        var payment = await VerifiedBytesAsync(owner);

        await migrations.MigrateAsync();
        await ExecuteAsync(owner, RuntimeGrant);
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(original, await ManualBytesAsync(owner));
        var review = await SingleCaseAsync(finance, purchase.Account);
        Assert.Equal("DuplicateFunds", review.GetProperty("classification").GetString());
        Assert.Equal(payment, await VerifiedBytesAsync(owner));
        var legacy = await SingleCaseAsync(finance, online.Account);
        Assert.Equal("LegacyNeedsReview", legacy.GetProperty("classification").GetString());
        using var response = await finance.SendAsync(HttpMethod.Get,
            $"{ReviewPath(online.Account)}/{legacy.GetProperty("caseId").GetGuid():D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
        var verifiedAt = detail.GetProperty("references").GetProperty("verifiedAt").GetDateTimeOffset();
        Assert.Equal(verifiedAt.AddSeconds(1), legacy.GetProperty("capturedAt").GetDateTimeOffset());
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments"));
        var runtime = fixture.Trial.Catalogue.Runtime;
        Assert.Equal(0L, await ReadScalarAsync(runtime, "SELECT count(*) FROM public.unmatched_payments"));
        Assert.Equal(0L, await ReadScalarAsync(runtime, "SELECT count(*) FROM public.payment_review_decisions"));
        var deny = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(owner, "TRUNCATE public.unmatched_payments CASCADE"));
        Assert.Equal("55000", deny.SqlState);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => migrations.MigrateAsync(previous));
        Assert.Equal("55000", Assert.IsType<PostgresException>(refused.InnerException).SqlState);
        Assert.Contains("20261117160000_UnmatchedPaymentReviews", await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(original, await ManualBytesAsync(owner));
        // Empty successor migrations may already have rolled back before the populated payment migration refuses.
        await migrations.MigrateAsync();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        await ExecuteAsync(owner, RuntimeGrant);
        AssertStatus(await StatusAsync(purchase.Payer.Client, purchase.Quote), "Verified", "Fulfilled", "Applying");
    }

    private static Task<object?> ManualBytesAsync(NpgsqlConnectionStringBuilder owner) => ReadScalarAsync(owner, """
        SELECT jsonb_agg(to_jsonb(r) ORDER BY receipt_id)::text FROM public.manual_payment_receipts r
        """);

    private static Task<object?> VerifiedBytesAsync(NpgsqlConnectionStringBuilder owner) => ReadScalarAsync(owner, """
        SELECT jsonb_agg(to_jsonb(v) ORDER BY attempt_id)::text FROM public.verified_payments v
        """);

    private static Task SeedLegacyClockSkewAsync(NpgsqlConnectionStringBuilder owner, Guid account, Guid attempt) =>
        ExecuteAsync(owner, $"""
            SELECT set_config('app.billing_account_id', '{account:D}', true),
                set_config('app.payment_attempt_id', '{attempt:D}', true);
            INSERT INTO public.verified_payments (attempt_id, account_id, order_id, provider_id, provider_account_id,
                environment, configuration_version, provider_payment_reference, amount_paise, currency, captured_at, verified_at)
            SELECT attempt_id, account_id, order_id, provider_id, provider_account_id, environment, configuration_version,
                'legacy-fixture-clock-skew', amount_paise, currency, created_at + interval '1 second', created_at
            FROM public.payment_attempts WHERE attempt_id = '{attempt:D}';
            INSERT INTO public.payment_events (event_id, account_id, attempt_id, order_id, kind, recorded_at)
            SELECT gen_random_uuid(), account_id, attempt_id, order_id, 'PaidNeedsReview', created_at
            FROM public.payment_attempts WHERE attempt_id = '{attempt:D}';
            """);

    // A second statement entry was retained by the old schema before the new reconciliation queue existed.
    private static Task SeedLegacyDuplicateAsync(NpgsqlConnectionStringBuilder owner) => ExecuteAsync(owner, """
        INSERT INTO public.manual_payment_receipts (receipt_id, account_id, order_id, operation_key, admin_id, session_id,
            mfa_verified_at, received_at, verified_at, amount_paise, currency, receiving_account_id, configuration_version,
            evidence_policy_version, environment, review_reason, protected_payload, reference_digest, quote_revision, snapshot_hash)
        SELECT gen_random_uuid(), account_id, order_id, gen_random_uuid(), admin_id, session_id,
            mfa_verified_at, received_at, verified_at, amount_paise, currency, receiving_account_id, configuration_version,
            evidence_policy_version, environment, 'DuplicateFunds', 'legacy-fixture-ciphertext', repeat('a', 64), quote_revision, snapshot_hash
        FROM public.manual_payment_receipts
        """);
}
