using System.Diagnostics;
using ErrorOr;
using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0501LicensingTests
{
    /// <summary>
    /// A projection writer and the issuer queue on the same licence fence behind a third session that
    /// holds it. Whichever queued first runs first, on independent connections, and the other sees it.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Issuance_serializes_with_a_restriction_for_the_same_account_or_shop(bool restrictionFirst, bool throughSibling)
    {
        var verified = At("2026-11-20T12:00:00Z");
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        using var siblingClient = CreateClient(host);
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30));
        var sibling = throughSibling ? await OpenShopAsync(siblingClient, verified, verified.AddDays(30), account: actor.Binding.Account) : null;
        _clock.Now = verified.AddHours(1);
        var (lease, captured) = await ReserveAsync(host, actor);
        var grant = Issued(captured).GrantId;
        var done = await SnapshotAsync(host, actor, lease);
        var suspendAt = _clock.Now;

        // The restriction is account-wide; delivered for this shop or for a sibling under the same account.
        var target = sibling?.Binding ?? actor.Binding;
        Task RestrictAsync() => ApplyAsync(target, verified.AddDays(30), revision: 2, restriction: "Trading", suspensionEffectiveAt: suspendAt);

        await using var holder = await RowHold.BeginAsync(Migrator,
            $"SELECT public.take_merchant_licence_fence('{target.Account:D}'::uuid, '{target.Shop:D}'::uuid, false)");
        var timer = Stopwatch.StartNew();
        var restriction = restrictionFirst ? RestrictAsync() : null;
        var issuance = restrictionFirst ? null : IssueAsync(host, actor, grant, done);
        await WaitForBlockedByAsync(restriction ?? (Task)issuance!, holder.Pid);
        restriction ??= RestrictAsync();
        issuance ??= IssueAsync(host, actor, grant, done);
        await WaitForLockWaitersAsync(2);
        Assert.Equal(0, (await EvidenceAsync(actor.Shop)).Permits);
        var queued = timer.Elapsed;
        await holder.ReleaseAsync();

        await restriction;
        var result = await issuance;
        var later = await IssueAsync(host, actor, grant, await SnapshotAsync(host, actor, lease));
        _output.WriteLine("restrictionFirst={0} throughSibling={1}: both queued behind backend {2} after {3:F0} ms, settled after {4:F0} ms; " +
            "issuance {5}, next attempt {6}", restrictionFirst, throughSibling, holder.Pid, queued.TotalMilliseconds,
            timer.Elapsed.TotalMilliseconds, Code(result), Code(later));

        Assert.Equal("Licence.TradingRestricted", Code(later));
        if (restrictionFirst)
        {
            Assert.Equal("Licence.TradingRestricted", Code(result));
            Assert.Equal(0, (await EvidenceAsync(actor.Shop)).Permits);
            return;
        }

        // The issuer ran first: one permit at the revisions it was fenced under, then the restriction.
        var permit = Issued(result);
        var signed = Issued(await VerifyAsync(host, actor, permit.SignedPermit, ScopeOf(actor, lease), suspendAt));
        Assert.Equal((1L, 1L, 1L, 1L), (signed.AccountRevision, signed.ShopRevision, signed.CurrentAccountRevision, signed.CurrentShopRevision));
        Assert.Equal(1, (await EvidenceAsync(actor.Shop)).Permits);
    }

    /// <summary>The issuer waits on the actor row; what committed under that lock decides.</summary>
    [Theory]
    [InlineData("revocation", "Auth.SessionInvalidated")]
    [InlineData("role-loss", "Auth.SessionInvalidated")]
    [InlineData("membership-change", "OfflinePermit.AuthorityChanged")]
    public async Task Authority_committed_while_the_issuer_waits_on_the_actor_denies_it(string change, string code)
    {
        var verified = At("2026-11-20T12:00:00Z");
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30));
        _clock.Now = verified.AddHours(1);
        var (lease, captured) = await ReserveAsync(host, actor);
        var done = await SnapshotAsync(host, actor, lease);
        var before = await EvidenceAsync(actor.Shop);

        var write = change switch
        {
            "revocation" => $"UPDATE auth_sessions SET revoked_at = now(), revocation_reason = 'p0501-under-lock' WHERE id = '{actor.Session:D}'",
            "role-loss" => $"UPDATE shop_memberships SET status = 'Suspended', authority_version = authority_version + 1 WHERE user_id = '{actor.User:D}'",
            _ => $"UPDATE shop_memberships SET authority_version = authority_version + 1 WHERE user_id = '{actor.User:D}'",
        };
        await using var holder = await RowHold.BeginAsync(Migrator, $"SELECT 1 FROM users WHERE id = '{actor.User:D}' FOR UPDATE; {write};");
        var issuance = IssueAsync(host, actor, Issued(captured).GrantId, done);
        await WaitForBlockedByAsync(issuance, holder.Pid);
        await holder.ReleaseAsync();

        Assert.Equal(code, Code(await issuance));
        Assert.Equal(before, await EvidenceAsync(actor.Shop));
    }

    [Fact]
    public async Task Deadline_reached_while_signing_rolls_the_permit_back()
    {
        var verified = At("2026-11-20T12:00:00Z");
        var deadline = verified.AddHours(48);
        var advance = false;
        using var host = CreateHost(configure: services =>
        {
            var inner = services.Last(x => x.ServiceType == typeof(IOfflinePermitSigner));
            services.AddSingleton<IOfflinePermitSigner>(provider => new WaitingSigner(
                (IOfflinePermitSigner)inner.ImplementationFactory!(provider), () => { if (advance) _clock.Now = deadline; }));
        });
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30));
        _clock.Now = deadline.AddSeconds(-1);
        var (lease, captured) = await ReserveAsync(host, actor);
        var done = await SnapshotAsync(host, actor, lease);
        var before = await EvidenceAsync(actor.Shop);

        // Admitted and computed at 11:59:59; the signature returns at 12:00:00, the deadline itself.
        advance = true;
        var late = await IssueAsync(host, actor, Issued(captured).GrantId, done);
        Assert.Equal("Licence.VerificationUnavailable", Code(late));
        Assert.Equal(before, await EvidenceAsync(actor.Shop));

        _clock.Now = deadline.AddSeconds(-1);
        var lateGrant = (await ReserveAsync(host, actor, "p0501-late-device")).Grant;
        Assert.Equal("Licence.VerificationUnavailable", Code(lateGrant));
        Assert.Equal(before, await EvidenceAsync(actor.Shop));
    }

    [Fact]
    public async Task Concurrent_first_completions_store_one_permit()
    {
        var verified = At("2026-11-20T12:00:00Z");
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30));
        _clock.Now = verified.AddHours(1);
        var (lease, captured) = await ReserveAsync(host, actor);
        var done = await SnapshotAsync(host, actor, lease);

        await using var holder = await RowHold.BeginAsync(Migrator,
            $"SELECT public.take_merchant_licence_fence('{actor.Binding.Account:D}'::uuid, '{actor.Shop:D}'::uuid, false)");
        var attempts = Enumerable.Range(0, 4).Select(_ => IssueAsync(host, actor, Issued(captured).GrantId, done)).ToArray();
        await WaitForLockWaitersAsync(attempts.Length);
        await holder.ReleaseAsync();

        var permits = (await Task.WhenAll(attempts)).Select(Issued).ToArray();
        Assert.Single(permits.Select(x => x.SignedPermit).Distinct());
        await using var db = Open(Migrator);
        Assert.Equal(permits[0].SignedPermit,
            await db.Set<OfflineLicensePermit>().Where(x => x.ShopId == actor.Shop).Select(x => x.SignedPermit).SingleAsync(Ct));
    }

    private async Task WaitForBlockedByAsync(Task operation, int holder)
    {
        await using var connection = new NpgsqlConnection(Migrator);
        await connection.OpenAsync(Ct);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!operation.IsCompleted)
        {
            await using var command = new NpgsqlCommand(
                "SELECT pid FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND @holder = ANY(pg_blocking_pids(pid)) LIMIT 1", connection);
            command.Parameters.AddWithValue("holder", holder);
            if (await command.ExecuteScalarAsync(timeout.Token) is int waiter)
            {
                _output.WriteLine("PostgreSQL pg_blocking_pids: backend {0} waits for backend {1}", waiter, holder);
                return;
            }
            await Task.Delay(10, timeout.Token);
        }
        Assert.Fail("The operation finished without waiting on the held lock.");
    }

    private async Task WaitForLockWaitersAsync(int count)
    {
        await using var connection = new NpgsqlConnection(Migrator);
        await connection.OpenAsync(Ct);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            await using var command = new NpgsqlCommand("""
                SELECT count(*)::int FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock' AND cardinality(pg_blocking_pids(pid)) > 0
                """, connection);
            if ((int)(await command.ExecuteScalarAsync(timeout.Token))! >= count) return;
            await Task.Delay(10, timeout.Token);
        }
    }

    /// <summary>Runs a callback while the issuer waits for its signature, as a slow remote key would.</summary>
    private sealed class WaitingSigner(IOfflinePermitSigner inner, Action whileSigning) : IOfflinePermitSigner
    {
        public async Task<OfflinePermitSignature?> SignAsync(string type, byte[] payload, CancellationToken ct)
        {
            var signature = await inner.SignAsync(type, payload, ct);
            whileSigning();
            return signature;
        }
    }

    /// <summary>An open owner transaction; disposal always ends it, committed only by <see cref="ReleaseAsync"/>.</summary>
    private sealed class RowHold : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private bool _ended;

        private RowHold(NpgsqlConnection connection) => _connection = connection;

        public int Pid => _connection.ProcessID;

        public static async Task<RowHold> BeginAsync(string connectionString, string statements)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(Ct);
            var hold = new RowHold(connection);
            try
            {
                await using var command = new NpgsqlCommand($"BEGIN; {statements}", connection);
                await command.ExecuteNonQueryAsync(Ct);
                return hold;
            }
            catch
            {
                await hold.DisposeAsync();
                throw;
            }
        }

        public async Task ReleaseAsync()
        {
            if (_ended) return;
            _ended = true;
            await using var command = new NpgsqlCommand("COMMIT", _connection);
            await command.ExecuteNonQueryAsync(Ct);
        }

        public async ValueTask DisposeAsync()
        {
            _ended = true;
            await _connection.DisposeAsync();
        }
    }
}
