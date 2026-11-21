using System.Net;
using System.Net.Http.Json;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0413dLicensingTests
{
    [Theory]
    [InlineData("Trading", "Active")]
    [InlineData("Unrestricted", "PendingActivation")]
    [InlineData("Unrestricted", "Outage")]
    public async Task Conditional_completion_accepts_only_proven_original_receipts(string restriction, string state)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        var end = _clock.Now.AddDays(2);
        var outage = state == "Outage";
        await ApplyAsync(source.Binding, end, restriction: restriction, state: outage ? "Active" : state,
            verified: outage ? _clock.Now.AddHours(-48) : null);
        await EnforceAsync(source.Shop);
        _clock.Now = _clock.Now.AddMinutes(1);
        if (outage)
            await AssertProblemAsync(await client.GetAsync($"/api/licensing/shops/{source.Shop}/trading-authority", Ct),
                HttpStatusCode.ServiceUnavailable, "Licence.VerificationUnavailable");

        var version = await SetReceiptInstantAsync(source.Receipt, _clock.Now.AddSeconds(1));
        await AssertProblemAsync(await CorrectAsync(client, source.Receipt, version, 1100, NewKey()),
            HttpStatusCode.Conflict, "PurchaseReceipt.SourceIneligible");
        version = await SetReceiptInstantAsync(source.Receipt, _clock.Now.AddSeconds(-1));
        using var success = await CorrectAsync(client, source.Receipt, version, 1100, null);
        Assert.Equal(100m, (await ReadSuccessAsync(success)).GetProperty("delta").GetDecimal());
    }

    [Fact]
    public async Task Observe_only_does_not_gate_an_existing_correction()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        await ApplyAsync(source.Binding, _clock.Now.AddDays(1), restriction: "Trading");
        var version = await SetReceiptInstantAsync(source.Receipt, _clock.Now.AddSeconds(1));
        using var response = await CorrectAsync(client, source.Receipt, version, 1100, NewKey());
        Assert.Equal(100m, (await ReadSuccessAsync(response)).GetProperty("delta").GetDecimal());
    }

    [Fact]
    public async Task Expired_correction_keeps_allocation_floor_and_rejects_unresolved_evidence()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        await PostAsync(client, $"/api/suppliers/{source.Supplier}/payments", new
        {
            amount = 950, paymentDate = BusinessCalendar.DateOf(_clock.Now), operationKey = NewKey(), method = "Cash",
            allocations = new[] { new { targetId = source.Receipt, amount = 950 } },
        });
        await ExpireAsync(client, source);
        var before = await FactsAsync(source);
        var version = await ReceiptVersionAsync(source.Receipt);
        await AssertProblemAsync(await CorrectAsync(client, source.Receipt, version, 949.99m, NewKey()),
            HttpStatusCode.Conflict, "Payment.CorrectionBelowAllocated");
        await AssertProblemAsync(await CorrectAsync(client, Guid.NewGuid(), version, 1100, NewKey()),
            HttpStatusCode.NotFound, "PurchaseReceipt.NotFound");
        Assert.Equal(before, await FactsAsync(source));
        Assert.Equal(950m, before.Allocated);

        using var response = await CorrectAsync(client, source.Receipt, version, 1000.01m, NewKey());
        Assert.Equal(0.01m, (await ReadSuccessAsync(response)).GetProperty("delta").GetDecimal());
        Assert.Equal(950m, (await FactsAsync(source)).Allocated);
        await AssertProblemAsync(await CorrectAsync(client, source.Receipt, version, 1050, NewKey()),
            HttpStatusCode.Conflict, "PurchaseReceipt.VersionConflict");
        version = await ReceiptVersionAsync(source.Receipt);
        await AssertProblemAsync(await CorrectAsync(client, source.Receipt, version, 1000.01m, NewKey()),
            HttpStatusCode.BadRequest, "PurchaseReceipt.CostUnchanged");

        await MarkLegacyUnresolvedAsync(source.Receipt);
        var unresolved = await FactsAsync(source);
        version = await ReceiptVersionAsync(source.Receipt);
        await AssertProblemAsync(await CorrectAsync(client, source.Receipt, version, 1050, NewKey()),
            HttpStatusCode.BadRequest, "PurchaseReceipt.EvidenceMissing");
        Assert.Equal(unresolved, await FactsAsync(source));
        _output.WriteLine("950 remains allocated; 949.99 rejected (409); +0.01 posted; stale version 409 and unresolved evidence 400 wrote no new facts.");
    }

    private async Task MarkLegacyUnresolvedAsync(Guid receipt)
    {
        await using var db = Open(Migrator);
        Assert.Equal(1, await db.Database.ExecuteSqlAsync($"""
            UPDATE purchase_receipt_values SET status = 'LEGACY_UNRESOLVED', source = 'LEGACY_BACKFILL',
                original_total_cost = NULL, effective_total_cost = NULL WHERE id = {receipt}
            """, Ct));
    }

    [Fact]
    public async Task Source_month_may_be_closed_but_the_frozen_correction_month_must_be_open()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        var sourceMonth = BusinessCalendar.DateOf(_clock.Now.AddMonths(-1));
        var version = await SetReceiptInstantAsync(source.Receipt, _clock.Now.AddMonths(-1));
        await PostAsync(client, $"/api/posting-periods/{sourceMonth:yyyy-MM}/close", new { reason = "Prior books complete", operationKey = NewKey() });
        await ExpireAsync(client, source);

        using var response = await CorrectAsync(client, source.Receipt, version, 1100, NewKey());
        Assert.Equal(100m, (await ReadSuccessAsync(response)).GetProperty("delta").GetDecimal());
        version = await ReceiptVersionAsync(source.Receipt);

        await CloseCorrectionPeriodInFixtureAsync(source.Shop);
        var before = await FactsAsync(source);
        await AssertProblemAsync(await CorrectAsync(client, source.Receipt, version, 1050, null),
            HttpStatusCode.Conflict, "PostingPeriod.Closed");
        Assert.Equal(before, await FactsAsync(source));
    }

    private async Task CloseCorrectionPeriodInFixtureAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        var month = PostingPeriod.MonthOf(BusinessCalendar.DateOf(_clock.Now));
        var row = await db.PostingPeriods.SingleAsync(x => x.ShopId == shop && x.StartDate == month.Start, Ct);
        var owner = await db.ShopMemberships.Where(x => x.ShopId == shop).Select(x => x.UserId).SingleAsync(Ct);
        var closed = row.Close(owner, _clock.Now.AddMonths(1), month.End.AddDays(1), "Correction books complete");
        Assert.False(closed.IsError);
        db.PostingPeriodEvents.Add(closed.Value);
        await db.SaveChangesAsync(Ct);
    }
}
