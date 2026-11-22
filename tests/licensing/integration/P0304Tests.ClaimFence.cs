using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;
using static Intelibill.Licensing.Integration.Tests.P0304Tests;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// A browser claim whose event is inserted but not yet committed must still fence an older proof-free read: the
/// stale unpaid answer cannot resume the order over a claim that commits after it.
/// </summary>
public sealed partial class P0304TestsRecovery
{
    private const long ClaimGateKey = 8_670_304;

    [Fact]
    public async Task StaleUnpaidReadCannotResumeOverAnUncommittedBrowserClaim()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        AssertStatus(await rig.VerifyAsync(attempt, "Success", null), "PendingVerification", "NotFulfilled", "NotApplicable");

        await using var gate = await ClaimGate.CloseAsync(rig.Fixture.Hosts.LicensingOwner);
        try
        {
            // The proof-free recheck reads the order unpaid, then its last answer is held in flight.
            rig.Fake.HoldWhen = call => call == $"GET /v1/orders/{order}/payments";
            var stale = rig.VerifyAsync(attempt, null, null);
            await rig.Fake.HoldReached.Task.WaitAsync(TimeSpan.FromSeconds(30));

            // A second browser claim inserts its event, then stops before its transaction commits.
            using var interrupt = new CancellationTokenSource();
            var claim = rig.Payer.Client.PostAsJsonAsync(VerifyPath(rig.Quote, attempt),
                new { launcherOutcome = "Cancelled", launcherProof = (object?)null }, interrupt.Token);
            await gate.WaitForWaitersAsync(1, null);

            rig.Fake.ReleaseHeld.SetResult();
            await gate.WaitForWaitersAsync(2, stale);
            // Once committed, the claim's own provider read never answers and its caller goes away.
            rig.Fake.StallWhen = call => call.StartsWith("GET ", StringComparison.Ordinal);
            await gate.OpenAsync();
            await rig.Fake.StallReached.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await interrupt.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => claim);
            rig.Fake.StallWhen = null;

            await AssertHeldWithoutLauncherAsync(rig, await stale);
        }
        finally
        {
            await gate.OpenAsync();
            await gate.DropAsync();
        }

        var resumed = await rig.VerifyAsync(attempt, null, null);
        AssertStatus(resumed, "AwaitingPayment", "NotFulfilled", "NotApplicable");
        Assert.Equal(order, resumed.GetProperty("launch").GetProperty("parameters").GetProperty("orderId").GetString());
        Assert.Equal(1, rig.Fake.Posts);
    }

    /// <summary>
    /// A test-only trigger that blocks a browser claim's event insert on an advisory lock this connection holds, so
    /// the claim transaction sits after its insert and before its commit until the gate opens.
    /// </summary>
    private sealed class ClaimGate(NpgsqlConnection holder, NpgsqlConnectionStringBuilder owner) : IAsyncDisposable
    {
        internal static async Task<ClaimGate> CloseAsync(NpgsqlConnectionStringBuilder owner)
        {
            await LicensingPostgreSqlFixture.ExecuteAsync(owner, $"""
                CREATE OR REPLACE FUNCTION public.p0304_claim_gate() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.kind = 'VerificationRequested' AND NEW.actor_id IS NOT NULL THEN
                        PERFORM set_config('lock_timeout', '0', true);
                        PERFORM pg_advisory_xact_lock({ClaimGateKey});
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER p0304_claim_gate AFTER INSERT ON public.payment_events
                    FOR EACH ROW EXECUTE FUNCTION public.p0304_claim_gate();
                """);
            var holder = new NpgsqlConnection(owner.ConnectionString);
            await holder.OpenAsync();
            await using var command = new NpgsqlCommand($"SELECT pg_advisory_lock({ClaimGateKey})", holder);
            await command.ExecuteNonQueryAsync();
            return new(holder, owner);
        }

        /// <summary>Waits until this many sessions wait on an advisory lock, or until <paramref name="done"/> ends.</summary>
        internal async Task WaitForWaitersAsync(long count, Task? done)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline && done?.IsCompleted != true)
            {
                var waiting = (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(owner, """
                    SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted
                        AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
                    """))!;
                if (waiting >= count)
                    return;
                await Task.Delay(20);
            }
            Assert.True(done?.IsCompleted == true, $"Fewer than {count} sessions reached an advisory lock.");
        }

        internal async Task OpenAsync()
        {
            await using var command = new NpgsqlCommand($"SELECT pg_advisory_unlock({ClaimGateKey})", holder);
            await command.ExecuteNonQueryAsync();
        }

        internal Task DropAsync() => LicensingPostgreSqlFixture.ExecuteAsync(owner, """
            DROP TRIGGER IF EXISTS p0304_claim_gate ON public.payment_events;
            DROP FUNCTION IF EXISTS public.p0304_claim_gate();
            """);

        public ValueTask DisposeAsync() => holder.DisposeAsync();
    }
}
