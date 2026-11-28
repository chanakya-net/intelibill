using System.Text.Json;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0701Tests
{
    private static async Task AssertDiscoveryIsolationAcrossScopesAsync(
        NpgsqlConnectionStringBuilder bootstrap,
        NpgsqlConnectionStringBuilder runtime,
        Guid accountId)
    {
        // Seed one active paid term on accountId so discovery has an eligible account to return in worker scope.
        var now = P0209Fixture.Utc("2028-02-01T04:30:00Z");
        var termId = Guid.NewGuid();
        await LicensingPostgreSqlFixture.ExecuteAsync(bootstrap, $"""
            INSERT INTO public.subscription_coverage_terms (term_id, account_id, mode, capacity, starts_at, ends_at)
            VALUES ('{termId:D}', '{accountId:D}', 'Independent', 1, '2028-01-31T04:30:00Z', '2028-02-29T04:30:00Z');
            INSERT INTO public.subscription_term_facts (
                account_id, term_id, basis, billing_period, anchor_local, time_zone, period_ordinal,
                member_allowance, price_paise, currency, plan_version_id, source_reference,
                development_fixture, calendar_version, access_policy_version, recorded_at)
            VALUES (
                '{accountId:D}', '{termId:D}', 'Paid', 'Month', '2028-01-31 10:00:00', 'Asia/Kolkata', 0,
                5, 12000, 'INR', NULL, 'payment-fulfilment/1', true, '{BillingCalendar.Version}', '{GracePolicy.Version}',
                '2028-01-31T04:30:00Z');
            """);

        await using var rt = new NpgsqlConnection(runtime.ConnectionString);
        await rt.OpenAsync();

        // A dedicated transaction-local planner marker permits discovery; other operation scopes still take precedence.
        Assert.Equal([accountId], await DiscoverInScopeAsync(rt, now, gucName: null, gucValue: null));

        Assert.Empty(await DiscoverInScopeAsync(rt, now, "app.notification_planner", ""));
        foreach (var (setting, value) in new[]
        {
            ("app.development_gateway", "on"), ("app.gateway_account", "simulator"),
            ("app.gateway_configuration", "development"), ("app.gateway_environment", "Testing"),
            ("app.catalogue_public_read", "on"), ("app.payment_attempt_id", Guid.NewGuid().ToString()),
            ("app.reduction_due_scan", "on"), ("app.entitlement_replay_id", Guid.NewGuid().ToString()),
            ("app.coverage_transfer_id", Guid.NewGuid().ToString()), ("app.trial_delivery_id", Guid.NewGuid().ToString()),
            ("app.manual_order_id", Guid.NewGuid().ToString()), ("app.slot_assignment_id", Guid.NewGuid().ToString()),
        })
            Assert.Empty(await DiscoverInScopeAsync(rt, now, setting, value));

        Assert.Empty(await DiscoverInScopeAsync(rt, now, "app.billing_actor_id", Guid.NewGuid().ToString("D")));
        Assert.Empty(await DiscoverInScopeAsync(rt, now, "app.billing_account_id", accountId.ToString("D")));
        Assert.Empty(await DiscoverInScopeAsync(rt, now, "app.status_shop_id", Guid.NewGuid().ToString("D")));
        Assert.Empty(await DiscoverInScopeAsync(rt, now, "app.provider_session_id", Guid.NewGuid().ToString("D")));
    }

    private static async Task AssertPlannerMemberContextAsync(NpgsqlConnectionStringBuilder runtime, Guid account, Guid owner)
    {
        await using var rt = new NpgsqlConnection(runtime.ConnectionString);
        await rt.OpenAsync();
        await using var tx = await rt.BeginTransactionAsync();
        await SetScopeGucAsync(rt, tx, "app.billing_account_id", account.ToString("D"));
        await using var read = new NpgsqlCommand(
            "SELECT merchant_user_id FROM public.account_notification_billing_members($1, NULL, 1)", rt, tx);
        read.Parameters.AddWithValue(account);
        Assert.Null(await read.ExecuteScalarAsync());
        await SetScopeGucAsync(rt, tx, "app.notification_planner", "on");
        Assert.Equal(owner, await read.ExecuteScalarAsync());
        await SetScopeGucAsync(rt, tx, "app.development_gateway", "on");
        await SetScopeGucAsync(rt, tx, "app.gateway_account", "fixture");
        await SetScopeGucAsync(rt, tx, "app.gateway_environment", "Development");
        await SetScopeGucAsync(rt, tx, "app.gateway_configuration", "1");
        Assert.Null(await read.ExecuteScalarAsync());
        await tx.RollbackAsync();
    }

    private static async Task<Guid[]> DiscoverInScopeAsync(
        NpgsqlConnection rt,
        DateTimeOffset now,
        string? gucName,
        string? gucValue)
    {
        await using var tx = await rt.BeginTransactionAsync();
        await SetScopeGucAsync(rt, tx, "app.notification_planner", "on");
        if (gucName is not null && gucValue is not null)
            await SetScopeGucAsync(rt, tx, gucName, gucValue);

        await using var cmd = new NpgsqlCommand("SELECT account_id FROM public.due_notification_planning($1, NULL, 20)", rt, tx);
        cmd.Parameters.AddWithValue(now);
        var list = new List<Guid>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                list.Add(reader.GetGuid(0));
        }

        await tx.RollbackAsync();
        return [.. list];
    }

    private static async Task SetScopeGucAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string name, string value)
    {
        await using var cmd = new NpgsqlCommand("SELECT set_config($1, $2, true)", conn, tx);
        cmd.Parameters.AddWithValue(name);
        cmd.Parameters.AddWithValue(value);
        await cmd.ExecuteNonQueryAsync();
    }
}
