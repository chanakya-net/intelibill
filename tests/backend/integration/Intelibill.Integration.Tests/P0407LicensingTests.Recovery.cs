using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.FinancialOperations.Queries.GetFinancialOperation;
using Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrderDetail;
using Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrders;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0407LicensingTests
{
    [Fact]
    public async Task Committed_receipt_recovery_checks_permission_before_disclosure()
    {
        using var host = CreateHost();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(10);
        await ApplyAsync(binding, end, securityEffectiveAt: end.AddMinutes(1));
        await EnforceAsync(shop);
        var (order, line) = await PlaceOrderAsync(client);
        var path = $"/api/purchase-orders/{order}/receipts";
        var key = Guid.NewGuid();
        var body = Receipt(line, "RECOVER", 4, key, _clock.Now);
        var original = await ReadSuccessAsync(await client.PostAsJsonAsync(path, body, Ct));
        var facts = await ReadFactsAsync(shop, order);

        _clock.Now = end;
        await AssertRecoveryDuringExpiryAsync(client, path, key, body, line, original);
        Assert.Equal(facts, await ReadFactsAsync(shop, order));

        _clock.Now = end.AddMinutes(1);
        await WithDirectScopeAsync(host, client, async (services, session) =>
        {
            var handler = ActivatorUtilities.CreateInstance<GetFinancialOperationQueryHandler>(services);
            foreach (var lookup in new[] { key, Guid.NewGuid() })
            {
                var outcome = await handler.Handle(
                    new(session.UserId, shop, "purchase-order-receipt", lookup.ToString("D"), session), Ct);
                Assert.True(outcome.IsError, "Security suspension disclosed the committed purchase receipt.");
                Assert.Equal("Licence.SecuritySuspended", outcome.FirstError.Code);
            }
            AssertCode("Licence.SecuritySuspended",
                await Handler<GetPurchaseOrderDetailQueryHandler>(services).HandleAsync(new(session.UserId, shop, order, session), Ct));
            AssertCode("Licence.SecuritySuspended",
                await Handler<GetPurchaseOrdersQueryHandler>(services).HandleAsync(new(session.UserId, shop, Session: session), Ct));
        });
        await AssertProblemAsync(await client.PostAsJsonAsync(path, body, Ct),
            HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        Assert.Equal(facts, await ReadFactsAsync(shop, order));
    }

    private async Task AssertRecoveryDuringExpiryAsync(HttpClient client, string path, Guid key, object body,
        Guid line, JsonElement original)
    {
        var replay = await ReadSuccessAsync(await client.PostAsJsonAsync(path, body, Ct));
        var lookup = await ReadSuccessAsync(await client.GetAsync($"/api/financial-operations/purchase-order-receipt/{key}", Ct));
        Assert.True(JsonElement.DeepEquals(original, replay));
        Assert.True(JsonElement.DeepEquals(original, lookup.GetProperty("response")));
        await AssertProblemAsync(await client.PostAsJsonAsync(path, Receipt(line, "CHANGED", 4, key, _clock.Now), Ct),
            HttpStatusCode.Conflict, "Operation.PayloadMismatch");
        await AssertProblemAsync(await client.GetAsync($"/api/financial-operations/purchase-order-receipt/{Guid.NewGuid()}", Ct),
            HttpStatusCode.NotFound, "Operation.NotFound");
        var history = await ReadSuccessAsync(await client.GetAsync(path[..^"/receipts".Length], Ct));
        Assert.Equal(original.GetProperty("receipts")[0].GetProperty("receiptId").GetGuid(),
            history.GetProperty("receipts")[0].GetProperty("receiptId").GetGuid());
        _output.WriteLine($"Recovery retained receipt {history.GetProperty("receipts")[0].GetProperty("receiptId")} and key {key:D}.");
    }

    private static async Task WithDirectScopeAsync(ApiWebApplicationFactory host, HttpClient client,
        Func<IServiceProvider, SessionClaims, Task> call)
    {
        var jwt = new JwtSecurityToken(client.DefaultRequestHeaders.Authorization!.Parameter);
        var subject = jwt.Claims.Single(claim => claim.Type == "sub").Value;
        var identity = new ClaimsIdentity(jwt.Claims.Append(new Claim(ClaimTypes.NameIdentifier, subject)), "Bearer");
        var accessor = host.Services.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            await call(scope.ServiceProvider, SessionClaims.TryRead(accessor.HttpContext.User)!);
        }
        finally
        {
            accessor.HttpContext = previous;
        }
    }

    private static async Task<(Guid Order, Guid Line)> PlaceOrderAsync(HttpClient client, DraftInput? input = null)
    {
        var draft = await CreateDraftAsync(client, input ?? await DraftInputAsync(client));
        var order = draft.GetProperty("purchaseOrderId").GetGuid();
        using var placed = await client.PostAsync($"/api/purchase-orders/{order}/place", null, Ct);
        await ReadSuccessAsync(placed);
        return (order, draft.GetProperty("lines")[0].GetProperty("lineId").GetGuid());
    }
}
