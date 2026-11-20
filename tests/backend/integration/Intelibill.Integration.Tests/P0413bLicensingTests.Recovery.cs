using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.FinancialOperations.Queries.GetFinancialOperation;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Services.Cancellations;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0413bLicensingTests
{
    [Theory]
    [InlineData("security")]
    [InlineData("expiry")]
    [InlineData("month")]
    public async Task Cancellation_rechecks_effective_time_after_business_lock_wait(string boundary)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var cutoff = boundary == "month" ? StartOfBusinessMonth(_clock.Now) : Deadline;
        var receipt = await SeedReceiptAsync(host, client, soldAt: boundary == "month" ? cutoff.AddMinutes(-1) : null);
        if (boundary == "month") _clock.Now = cutoff.AddMinutes(-1);
        var binding = await BindAsync(receipt.Shop);
        var security = boundary == "security";
        await ApplyAsync(binding, security ? cutoff.AddHours(1) : cutoff, restriction: security ? "Security" : "Unrestricted",
            effective: security ? cutoff : null);
        await EnforceAsync(receipt.Shop);

        await using var holder = Open(Migrator);
        await using var saleLock = await holder.Database.BeginTransactionAsync(Ct);
        await holder.Database.ExecuteSqlAsync($"SELECT id FROM sales WHERE id = {receipt.Sale.Id} FOR NO KEY UPDATE", Ct);
        var holderPid = await PidAsync(holder);
        var pending = client.PostAsJsonAsync(receipt.Path + "/cancellations", receipt.Command(), Ct);
        await using var delivery = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        Task? restriction = null;
        try
        {
            var sourcePid = await WaitForBlockedPidAsync(holderPid, pending);
            var status = pending.IsCompletedSuccessfully ? (int)(await pending).StatusCode : (int?)null;
            Assert.True(sourcePid is not null,
                $"Cancellation did not wait on the sale row. Completed HTTP status: {status}.");
            await AssertHeldAccountFenceAsync(binding);
            if (boundary == "expiry")
            {
                await delivery.Database.OpenConnectionAsync(Ct);
                var deliveryPid = await PidAsync(delivery);
                _clock.Now = cutoff.AddSeconds(-1);
                restriction = ApplyAsync(binding, cutoff.AddHours(1), 2, "Security", connection: delivery, effective: cutoff);
                Assert.Equal(deliveryPid, await WaitForBlockedPidAsync(sourcePid!.Value, restriction));
                _output.WriteLine($"PostgreSQL: projection PID {deliveryPid} queued behind source PID {sourcePid}.");
            }
            _clock.Now = cutoff;
            _output.WriteLine($"PostgreSQL: sale lock holder PID {holderPid}; cancellation PID {sourcePid}; " +
                $"account fence held before sale lock; {boundary} advanced to {cutoff:O}.");
        }
        finally
        {
            await saleLock.RollbackAsync(Ct);
        }

        using var result = await pending;
        if (boundary == "expiry")
        {
            await ReadSuccessAsync(result);
            await AssertPostedExactlyOnceAsync(receipt);
            await restriction!;
            await AssertProblemAsync(await client.PostAsJsonAsync(receipt.Path + "/cancellations", receipt.Command(), Ct),
                HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
            return;
        }
        await AssertProblemAsync(result, security ? HttpStatusCode.Forbidden : HttpStatusCode.Conflict,
            security ? "Licence.SecuritySuspended" : "SaleCancellation.OtherMonthBlocked");
        await AssertUnchangedAsync(receipt);
    }

    [Fact]
    public async Task Cancellation_replay_and_lookup_require_current_recovery_authority()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        using var manager = CreateClient(host);
        var receipt = await SeedReceiptAsync(host, client);
        await AddMemberAsync(host, client, manager, receipt.Shop, "Manager");
        var binding = await ExpireAsync(receipt);
        var route = receipt.Path + "/cancellations";
        const string key = "cancel-892";
        const string outcomes = "/api/financial-operations/sale-cancellation/";
        var command = receipt.Command(key);
        await AssertProblemAsync(await manager.GetAsync(outcomes + key, Ct), HttpStatusCode.NotFound, "Operation.NotFound");
        using var created = await manager.PostAsJsonAsync(route, command, Ct);
        var original = await ReadSuccessAsync(created);
        await AssertProblemAsync(await client.PostAsJsonAsync(route, receipt.Command("second"), Ct),
            HttpStatusCode.Conflict, "SaleCancellation.AlreadyCancelled");

        Rollout(host).Enabled = false;
        await CloseMonthAsync(receipt);
        using var replay = await client.PostAsJsonAsync(route, command, Ct);
        Assert.True(JsonElement.DeepEquals(original, await ReadSuccessAsync(replay)));
        using var recovered = await client.GetAsync(outcomes + key, Ct);
        var lookup = (await ReadSuccessAsync(recovered)).GetProperty("response");
        Assert.Equal(original.GetProperty("id").GetGuid(), lookup.GetProperty("id").GetGuid());
        await using (var db = Open(Migrator))
        {
            var stored = await db.OperationReceipts.SingleAsync(x => x.ShopId == receipt.Shop && x.OperationKey == key, Ct);
            using var payload = JsonDocument.Parse(stored.ResponseBody!);
            Assert.True(JsonElement.DeepEquals(payload.RootElement, lookup));
        }
        await AssertProblemAsync(await client.PostAsJsonAsync(route, receipt.Command(key, reason: "Changed payload"), Ct),
            HttpStatusCode.Conflict, "Operation.PayloadMismatch");

        await ApplyAsync(binding, Deadline.AddHours(1), 2, "Security");
        await AssertProblemAsync(await client.PostAsync(receipt.Path + "/cancellation-preview", null, Ct),
            HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        await AssertProblemAsync(await client.PostAsJsonAsync(route, command, Ct),
            HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        await AssertProblemAsync(await client.GetAsync(outcomes + key, Ct), HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        await AssertDirectSecurityRefusalsAsync(host, receipt);
        await ApplyAsync(binding, Deadline.AddHours(1), 3, released: 2);
        using var released = await manager.PostAsJsonAsync(route, command, Ct);
        Assert.True(JsonElement.DeepEquals(original, await ReadSuccessAsync(released)));
        await AssertPostedExactlyOnceAsync(receipt);
    }

    private async Task AssertDirectSecurityRefusalsAsync(ApiWebApplicationFactory host, Receipt receipt)
    {
        var accessor = host.Services.GetRequiredService<IHttpContextAccessor>();
        var original = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, receipt.Actor.ToString()),
                new Claim("active_shop_id", receipt.Shop.ToString()),
            ])),
        };
        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<SaleCancellationService>();
            var preview = await service.PreviewAsync(receipt.Actor, receipt.Shop, receipt.Sale.Id, Ct);
            Assert.Equal(Errors.Licence.SecuritySuspended.Code, preview.FirstError.Code);
            var replay = await service.CancelAsync(receipt.Actor, receipt.Shop, receipt.Sale.Id,
                new CancelSaleRequest("cancel-892", receipt.Sale.Version, "Recovered all goods", true, true), Ct);
            Assert.Equal(Errors.Licence.SecuritySuspended.Code, replay.FirstError.Code);
            var handler = ActivatorUtilities.CreateInstance<GetFinancialOperationQueryHandler>(scope.ServiceProvider);
            var lookup = await handler.Handle(new(receipt.Actor, receipt.Shop, "sale-cancellation", "cancel-892"), Ct);
            Assert.Equal(Errors.Licence.SecuritySuspended.Code, lookup.FirstError.Code);

            await using var owner = Open(Migrator);
            await owner.ShopMemberships.Where(x => x.UserId == receipt.Actor && x.ShopId == receipt.Shop)
                .ExecuteUpdateAsync(x => x.SetProperty(m => m.Role, ShopRole.Staff), Ct);
            replay = await service.CancelAsync(receipt.Actor, receipt.Shop, receipt.Sale.Id,
                new CancelSaleRequest("cancel-892", receipt.Sale.Version, "Recovered all goods", true, true), Ct);
            Assert.Equal(Errors.SaleCancellation.Forbidden.Code, replay.FirstError.Code);
            lookup = await handler.Handle(new(receipt.Actor, receipt.Shop, "sale-cancellation", "cancel-892"), Ct);
            Assert.Equal(Errors.Operation.UserIsNotOwnerOrManager.Code, lookup.FirstError.Code);
        }
        finally
        {
            accessor.HttpContext = original;
        }
    }

    private async Task AssertHeldAccountFenceAsync(Binding binding)
    {
        await using var db = Open(Migrator);
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        var acquired = await db.Database.SqlQuery<bool>($"""
            SELECT pg_try_advisory_xact_lock(hashtextextended({'a' + binding.Account.ToString("D")}, 859)) AS "Value"
            """).SingleAsync(Ct);
        Assert.False(acquired);
    }

    private static DateTimeOffset StartOfBusinessMonth(DateTimeOffset instant)
    {
        var india = TimeSpan.FromMinutes(330);
        var local = instant.ToOffset(india);
        return new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, india).ToUniversalTime();
    }
}
