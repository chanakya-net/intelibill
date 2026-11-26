using System.Security.Cryptography;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0607aTests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0607Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FinalConsentEpochAndConsumeReceiptSerializeInBothOrders(bool applyFirst)
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var id = await ConsentedAsync(s, s.Permit.ValidUntil);
        var (manager, _) = await s.F.Checkout.Trial.Catalogue.ManagerAsync("race@example.test");
        using var session = manager;
        var key = await ApproveAsync(s, id, manager);
        await DrainAsync(s);
        var delivery = s.F.Checkout.Delivery;
        s.F.At(s.Permit.ValidUntil);
        s.F.Hosts.Fixture.Clock.Now = s.Permit.ValidUntil;
        delivery.Transport.LeaseClock.Now = s.Permit.ValidUntil.AddSeconds(1);
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        Assert.Equal(1L, await delivery.MerchantScalarAsync($"SELECT count(*) FROM merchant_transfer_decisions WHERE transfer_id='{id}' AND outcome='Consumed'"));
        // Isolate this signed receipt from unrelated verification traffic at the same clock boundary.
        await LicensingPostgreSqlFixture.ExecuteAsync(delivery.MerchantDb, $"""
            UPDATE entitlement_outbox o SET next_attempt_at='2030-01-01' FROM entitlement_messages m
            WHERE m.event_id=o.event_id AND m.kind='Acknowledgement' AND m.account_id IN ('{s.Source.Account}','{s.Destination.Account}') AND o.status='Pending'
            """);
        await RaceFinalReceiptAsync(s, applyFirst);
        await DrainAsync(s);
        await AssertStateAsync(manager, AdminPath(s, id), key, applyFirst ? "Applied" : "NeedsReview");
        Assert.Equal(applyFirst ? s.Destination.Account : s.Source.Account, await BindingAsync(s));
        Assert.Equal(applyFirst ? 1L : 0L, await s.F.CountAsync("coverage_transfer_bindings"));
    }

    private static async Task RaceFinalReceiptAsync(Scenario s, bool applyFirst)
    {
        var account = s.Source.Account;
        var acquire = $"SELECT pg_advisory_xact_lock(hashtextextended('b{account:D}',0))";
        await using var gate = new NpgsqlConnection(s.F.Hosts.LicensingOwner.ConnectionString);
        await gate.OpenAsync();
        await using var tx = await gate.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand(acquire, gate, tx);
        await hold.ExecuteNonQueryAsync();
        Task Receipt() => s.F.Checkout.Delivery.DrainAsync(false);
        Task RotateAuthority() => LicensingPostgreSqlFixture.ExecuteAsync(s.F.Hosts.LicensingOwner, $"""
            BEGIN;
            {acquire};
            UPDATE billing_account_members SET revision=revision+1
            WHERE account_id='{account}' AND merchant_user_id='{s.Source.Payer.UserId}';
            COMMIT
            """);
        var first = applyFirst ? Receipt() : RotateAuthority();
        await WaitForBlockedAsync(s, 1);
        var second = applyFirst ? RotateAuthority() : Receipt();
        await WaitForBlockedAsync(s, 2);
        await tx.CommitAsync();
        await Task.WhenAll(first, second);
    }

    private static async Task WaitForBlockedAsync(Scenario s, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while ((long)(await s.F.ScalarAsync("""
            SELECT count(*) FROM pg_stat_activity WHERE datname=current_database()
                AND wait_event='advisory' AND cardinality(pg_blocking_pids(pid))>0
            """))! < count)
            await Task.Delay(10, timeout.Token);
    }
}
