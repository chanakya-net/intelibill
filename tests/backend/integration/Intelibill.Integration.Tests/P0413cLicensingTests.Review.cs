using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0413cLicensingTests
{
    [Fact]
    public async Task Shipment_uses_frozen_cutoff_and_equal_time_source_can_receive_credit()
    {
        var step = new ClockStep("FROM supplier_returns", _clock);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClockClient(host, step);
        var source = await SeedAsync(client);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(source.Binding, end);
        await EnforceAsync(source.Shop);

        var received = end.AddSeconds(1);
        await using var db = Open(Migrator);
        Assert.Equal(1, await db.Database.ExecuteSqlAsync(
            $"UPDATE purchase_receipt_values SET received_at = {received} WHERE id = {source.Receipt}", Ct));
        var (route, body) = await PrepareAsync(client, source, "shipment");
        var before = await FactsAsync(source.Shop);

        _clock.Now = end;
        step.Arm(received.AddSeconds(1));
        await AssertProblemAsync(await client.PostAsJsonAsync(route, body, Ct),
            HttpStatusCode.Conflict, "SupplierReturn.SourceIneligible");
        Assert.True(step.Stepped);
        Assert.Equal(before, await FactsAsync(source.Shop));

        _clock.Now = received;
        var pending = await PostAsync(client, route, body);
        Assert.Equal(received, pending.GetProperty("shippedAt").GetDateTimeOffset());
        _clock.Now = received.AddMinutes(1);
        var accepted = await PostAsync(client, $"/api/supplier-returns/{pending.GetProperty("returnId").GetGuid()}/credit",
            Credit(pending, NewKey()));
        Assert.Equal("CreditAccepted", accepted.GetProperty("status").GetString());
        Assert.Equal(before with { Operations = before.Operations + 2, Shipments = 1, Credits = 1,
            Movements = before.Movements + 1, Ledger = before.Ledger + 1, Stock = 7 }, await FactsAsync(source.Shop));
        _output.WriteLine("Shipment {0:O}, receipt {1:O}, final clock {2:O}. No rejected facts. Equal-time shipment and later credit committed.",
            end, received, received.AddSeconds(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reversal_uses_frozen_cutoff_for_every_lot_and_allows_equality(bool grouped)
    {
        var step = new ClockStep("FROM inventory_batches", _clock);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClockClient(host, step);
        var source = await SeedAsync(client);
        var group = grouped ? await ReceiveGroupAsync(client, source) : null;
        var end = _clock.Now.AddMinutes(1);
        var received = end.AddSeconds(1);
        var lot = group?.Lots.Max() ?? source.Receipt;
        await using var db = Open(Migrator);
        Assert.Equal(1, await db.Database.ExecuteSqlAsync(
            $"UPDATE purchase_receipt_values SET received_at = {received} WHERE id = {lot}", Ct));
        var (route, body) = group is null
            ? await PrepareAsync(client, source, "receipt")
            : (GroupRoute(group), await GroupPayloadAsync(client, group));
        await ApplyAsync(source.Binding, end);
        await EnforceAsync(source.Shop);
        var before = await FactsAsync(source.Shop);

        _clock.Now = end;
        step.Arm(received.AddSeconds(1));
        await AssertProblemAsync(await client.PostAsJsonAsync(route, body, Ct),
            HttpStatusCode.Conflict, "SupplierReturn.SourceIneligible");
        Assert.True(step.Stepped);
        Assert.Equal(before, await FactsAsync(source.Shop));

        _clock.Now = received;
        var result = await PostAsync(client, route, body);
        var lots = group?.Lots.Length ?? 1;
        foreach (var reversed in result.GetProperty("lots").EnumerateArray())
            Assert.Equal(received, reversed.GetProperty("reversedAt").GetDateTimeOffset());
        Assert.Equal(before with { Operations = before.Operations + 1, Reversals = lots,
            Movements = before.Movements + lots, Ledger = before.Ledger + lots, Stock = before.Stock - 10 },
            await FactsAsync(source.Shop));
        _output.WriteLine("Reversal {0:O}, receipt {1:O}, final clock {2:O}. No rejected facts; {3} lot(s) reversed at equality.",
            end, received, received.AddSeconds(1), lots);
    }

    [Fact]
    public async Task Credit_uses_frozen_cutoff_and_allows_shipment_at_equality()
    {
        var step = new ClockStep("FROM supplier_returns", _clock, whenTrackingCredit: true);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClockClient(host, step);
        var source = await SeedAsync(client);
        var end = _clock.Now.AddMinutes(1);
        var shipped = end.AddSeconds(1);
        await ApplyAsync(source.Binding, end);
        await EnforceAsync(source.Shop);
        _clock.Now = shipped;
        var (route, body) = await PrepareAsync(client, source, "credit");
        var before = await FactsAsync(source.Shop);

        _clock.Now = end;
        step.Arm(shipped.AddSeconds(1));
        await AssertProblemAsync(await client.PostAsJsonAsync(route, body, Ct),
            HttpStatusCode.Conflict, "SupplierReturn.SourceIneligible");
        Assert.True(step.Stepped);
        Assert.Equal(before, await FactsAsync(source.Shop));
        Assert.Equal("PendingSupplierCredit", (await GetAsync(client, route[..^"/credit".Length]))
            .GetProperty("status").GetString());

        _clock.Now = shipped;
        var accepted = await PostAsync(client, route, body);
        Assert.Equal(shipped, accepted.GetProperty("credit").GetProperty("acceptedAt").GetDateTimeOffset());
        Assert.Equal(before with { Operations = before.Operations + 1, Credits = 1, Ledger = before.Ledger + 1 },
            await FactsAsync(source.Shop));
        _output.WriteLine("Credit {0:O}, shipment {1:O}, final clock {2:O}. No rejected facts; credit posted at equality.",
            end, shipped, shipped.AddSeconds(1));
    }

    [Fact]
    public async Task Committed_credit_hash_is_compared_before_resolving_an_absent_target()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        var (route, body) = await PrepareAsync(client, source, "credit");
        await PostAsync(client, route, body);
        var before = await FactsAsync(source.Shop);

        await AssertProblemAsync(await client.PostAsJsonAsync($"/api/supplier-returns/{Guid.NewGuid()}/credit", body, Ct),
            HttpStatusCode.Conflict, "Operation.PayloadMismatch");
        Assert.Equal(before, await FactsAsync(source.Shop));
        _output.WriteLine("Absent credit target returned 409 Operation.PayloadMismatch with unchanged facts.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fresh_credit_observes_winning_security_for_present_or_absent_target(bool absent)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        var end = _clock.Now.AddDays(1);
        await ApplyAsync(source.Binding, end);
        await EnforceAsync(source.Shop);
        var (route, body) = await PrepareAsync(client, source, "credit");
        if (absent) route = $"/api/supplier-returns/{Guid.NewGuid()}/credit";
        var before = await FactsAsync(source.Shop);

        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "entitlement_snapshot_inbox", source.Shop);
        await using var delivery = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        var projection = ApplyAsync(source.Binding, end, 2, connection: delivery, securityEffectiveAt: _clock.Now);
        var blocker = await barrier.WaitForArrivalAsync();
        var request = client.PostAsJsonAsync(route, body, Ct);
        var waiter = await ObserveWaitAsync(request, blocker, barrier, projection);

        await AssertProblemAsync(await request, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        Assert.NotEqual(0, waiter);
        Assert.Equal(before, await FactsAsync(source.Shop));
        _output.WriteLine("Fresh credit (target absent: {0}) returned 403 Licence.SecuritySuspended with unchanged facts.", absent);
    }

    [Theory]
    [InlineData("shipment")]
    [InlineData("credit")]
    [InlineData("receipt")]
    [InlineData("group")]
    public async Task Duplicate_completion_rechecks_security_after_reading_the_committed_outcome(string operation)
    {
        var step = new ClockStep("FROM operation_receipts", _clock);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClockClient(host, step);
        var source = await SeedAsync(client);
        var (route, body) = await PrepareAsync(client, source, operation);
        var end = _clock.Now.AddMinutes(1);
        var effective = end.AddMinutes(1);
        await ApplyAsync(source.Binding, end, securityEffectiveAt: effective);
        await EnforceAsync(source.Shop);
        _clock.Now = end;

        var before = await FactsAsync(source.Shop);
        var outcome = await AssertConcurrentReplayAsync(client, route, body, source.Shop);
        Assert.True(JsonElement.DeepEquals(outcome, await PostAsync(client, route, body)));
        var committed = await FactsAsync(source.Shop);
        Assert.Equal(before.Operations + 1, committed.Operations);

        step.Arm(effective);
        await AssertProblemAsync(await client.PostAsJsonAsync(route, body, Ct),
            HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        Assert.True(step.Stepped);
        Assert.Equal(committed, await FactsAsync(source.Shop));
        _output.WriteLine("{0}: expired replay converged; security took effect during the outcome read and returned 403 with unchanged facts.",
            operation);
    }

    private async Task<JsonElement> AssertConcurrentReplayAsync(HttpClient client, string route, object body, Guid shop)
    {
        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "operation_receipts", shop);
        var first = PostAsync(client, route, body);
        Task<JsonElement>? second = null;
        try
        {
            var winner = await barrier.WaitForArrivalAsync();
            second = PostAsync(client, route, body);
            var waiter = await barrier.WaitForBlockedByAsync(winner);
            _output.WriteLine("Duplicate request PID {0} waited on reservation PID {1}.", waiter, winner);
        }
        finally
        {
            await barrier.ReleaseAsync();
        }
        var outcome = await first;
        Assert.True(JsonElement.DeepEquals(outcome, await second!));
        return outcome;
    }

    private static HttpClient CreateClockClient(ApiWebApplicationFactory host, ClockStep step)
    {
        var configured = host.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IInterceptor>(step)));
        var client = configured.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        IdentityTestMailbox.Factories.Add(client, configured);
        return client;
    }

    private sealed class ClockStep(string match, TestClock clock, bool whenTrackingCredit = false) : DbCommandInterceptor
    {
        private long _next;
        public bool Stepped { get; private set; }

        public void Arm(DateTimeOffset next) => Interlocked.Exchange(ref _next, next.UtcTicks);

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(match, StringComparison.Ordinal) &&
                Interlocked.Exchange(ref _next, 0) is > 0 and var ticks)
            {
                if (whenTrackingCredit)
                {
                    var tracker = eventData.Context!.ChangeTracker;
                    tracker.Tracked += OnTracked;

                    void OnTracked(object? sender, EntityTrackedEventArgs args)
                    {
                        if (args.Entry.Entity is not SupplierReturnCredit || args.FromQuery) return;
                        tracker.Tracked -= OnTracked;
                        Advance(ticks);
                    }
                }
                else Advance(ticks);
            }
            return ValueTask.FromResult(result);
        }

        private void Advance(long ticks)
        {
            clock.Now = new DateTimeOffset(ticks, TimeSpan.Zero);
            Stepped = true;
        }
    }
}
