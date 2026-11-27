using System.Net;
using System.Net.Http.Json;
using ErrorOr;
using Intelibill.Application.Features.PurchaseOrders.Commands.CreatePurchaseOrderDraft;
using Intelibill.Application.Features.PurchaseOrders.Commands.PlacePurchaseOrder;
using Intelibill.Application.Features.PurchaseOrders.Commands.ReceivePurchaseOrder;
using Intelibill.Application.Features.PurchaseOrders.Commands.UpdatePurchaseOrderDraft;
using Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrderDetail;
using Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrders;
using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0407LicensingTests
{
    [Fact]
    public async Task Direct_handlers_check_current_trading_scope_and_history()
    {
        using var host = CreateHost();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var deadline = _clock.Now.AddHours(1);
        await ApplyAsync(await BindAsync(shop), deadline);
        await EnforceAsync(shop);
        var input = await DraftInputAsync(client);
        var (order, line) = await PlaceOrderAsync(client, input);
        var draft = (await CreateDraftAsync(client, input)).GetProperty("purchaseOrderId").GetGuid();
        var before = await ReadFactsAsync(shop, order);
        _clock.Now = deadline;

        await WithDirectScopeAsync(host, client, async (services, session) =>
        {
            var actor = session.UserId;
            var create = Handler<CreatePurchaseOrderDraftCommandHandler>(services);
            var update = Handler<UpdatePurchaseOrderDraftCommandHandler>(services);
            var place = Handler<PlacePurchaseOrderCommandHandler>(services);
            var receive = Handler<ReceivePurchaseOrderCommandHandler>(services);
            var lot = new ReceivePurchaseOrderLineInput(line, "DIRECT", "LOT-DIRECT", 4, 40, 12, 11, 5, false, false, null, null);

            AssertCode("Licence.Expired", await create.HandleAsync(new(actor, shop, input.SupplierId, null, null, null,
                input.Notes, null, null, [.. input.Lines.Select(x => new CreatePurchaseOrderLineInput(x.ItemId, x.Description, 10, 10))], session), Ct));
            AssertCode("Licence.Expired", await update.HandleAsync(new(actor, shop, draft, input.SupplierId, null, null, null,
                "Attempted edit", [.. input.Lines.Select(x => new UpdatePurchaseOrderLineInput(x.ItemId, x.Description, 10, 10))], session), Ct));
            AssertCode("Licence.Expired", await place.HandleAsync(new(actor, shop, draft, session), Ct));
            AssertCode("Licence.Expired", await receive.HandleAsync(new(actor, shop, order, null, null, deadline.AddMinutes(-10),
                [lot], Guid.NewGuid(), session), Ct));
            AssertCode("Auth.SessionInvalidated", await receive.HandleAsync(new(actor, Guid.NewGuid(), order, null, null, null,
                [lot], Guid.NewGuid(), session), Ct));
            AssertCode("Auth.SessionInvalidated", await place.HandleAsync(new(actor, shop, draft), Ct));
            Assert.False((await Handler<GetPurchaseOrderDetailQueryHandler>(services).HandleAsync(new(actor, shop, order, session), Ct)).IsError);
            Assert.False((await Handler<GetPurchaseOrdersQueryHandler>(services).HandleAsync(new(actor, shop, Session: session), Ct)).IsError);
        });
        Assert.Equal(before, await ReadFactsAsync(shop, order));
    }

    [Theory]
    [InlineData("PendingActivation", "Unrestricted", 0, true, HttpStatusCode.Forbidden, "Licence.PendingActivation")]
    [InlineData("Active", "Trading", 0, true, HttpStatusCode.Forbidden, "Licence.TradingRestricted")]
    [InlineData("Active", "Unrestricted", 48, true, HttpStatusCode.ServiceUnavailable, "Licence.VerificationUnavailable")]
    [InlineData("Closed", "Unrestricted", 0, true, HttpStatusCode.Forbidden, "Licence.TradingRestricted")]
    [InlineData("Grace", "Unrestricted", 0, true, HttpStatusCode.OK, null)]
    [InlineData("Active", "Unrestricted", 48, false, HttpStatusCode.OK, null)]
    public async Task Receipt_uses_current_projection_and_test_enrollment(string state, string restriction, int hoursPassed,
        bool enforce, HttpStatusCode expected, string? code)
    {
        using var host = CreateHost();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var (order, line) = await PlaceOrderAsync(client);
        await ApplyAsync(await BindAsync(shop), _clock.Now.AddDays(7), restriction: restriction, state: state);
        await using (var db = Open(Migrator))
        {
            var projection = await db.Set<ShopLicenseState>().SingleAsync(x => x.ShopId == shop, Ct);
            Assert.Null(projection.EnforcementActivatedAt);
        }
        if (enforce) await EnforceAsync(shop);

        var before = await ReadFactsAsync(shop, order);
        var postingDate = _clock.Now;
        _clock.Now = _clock.Now.AddHours(hoursPassed);
        using var response = await client.PostAsJsonAsync($"/api/purchase-orders/{order}/receipts",
            Receipt(line, "CONTROL", 4, Guid.NewGuid(), postingDate), Ct);
        if (code is null)
        {
            Assert.Equal(expected, response.StatusCode);
            return;
        }

        await AssertProblemAsync(response, expected, code);
        Assert.Equal(before, await ReadFactsAsync(shop, order));
    }

    private static T Handler<T>(IServiceProvider services) where T : class => ActivatorUtilities.CreateInstance<T>(services);

    private static void AssertCode<T>(string code, ErrorOr<T> outcome)
    {
        Assert.True(outcome.IsError, $"Expected {code}, received success.");
        Assert.Equal(code, outcome.FirstError.Code);
    }
}
