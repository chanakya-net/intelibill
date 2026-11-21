using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Api.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0415LicensingTests
{
    [Fact]
    public Task Prepared_history_is_withheld_after_applied_security() =>
        AssertPreparedResultWithheldAsync("GetSaleDetail", sale => $"/api/sales/{sale:D}");

    [Fact]
    public Task Prepared_export_is_withheld_after_applied_security() =>
        AssertPreparedResultWithheldAsync("ExportSales", _ => "/api/exports/sales?format=xlsx");

    [Fact]
    public Task Prepared_domain_problem_withholds_its_stored_identifier_after_applied_security()
    {
        var storedId = Guid.NewGuid();
        var problem = new List<Error>
        {
            Error.NotFound("Fixture.StoredItemMissing", $"Missing inventory for stored item {storedId:D}."),
        }.ToProblemResult();
        return AssertPreparedResultWithheldAsync("GetSaleDetail", sale => $"/api/sales/{sale:D}", problem, storedId);
    }

    private async Task AssertPreparedResultWithheldAsync(string action, Func<Guid, string> path,
        IActionResult? result = null, Guid? storedId = null)
    {
        using var key = RSA.Create(2048);
        var pause = new PauseResult(action, result);
        using var host = CreateHost(key, services => services.AddControllers(options => options.Filters.Add(pause)));
        await host.InitializeAsync();
        using var client = CreateClient(host);
        using var delivery = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var grant = await BindAsync(shop);
        await DeliverAsync(delivery, key, grant);
        await EnforceAsync(shop);
        var sale = await CreateSaleAsync(client);

        var pending = client.GetAsync(path(sale), Ct);
        try
        {
            await pause.Ready.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
            await DeliverAsync(delivery, key, grant with
            {
                EventId = Guid.NewGuid(),
                Account = grant.Account! with { Revision = 2, Restriction = "Security" },
            });
        }
        finally
        {
            pause.Resume.TrySetResult();
        }

        using var response = await pending;
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Null(response.Content.Headers.ContentDisposition);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        if (storedId is { } id)
            Assert.DoesNotContain(id.ToString("D"), raw, StringComparison.OrdinalIgnoreCase);
        var body = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal("Licence.SecuritySuspended", body.GetProperty("code").GetString());
        Assert.False(body.TryGetProperty("document", out _));
        Assert.False(body.TryGetProperty("customerName", out _));
    }

    [Fact]
    public async Task Existing_history_follows_the_enrolled_action_matrix()
    {
        using var key = RSA.Create(2048);
        using var host = CreateHost(key);
        await host.InitializeAsync();
        using var merchant = CreateClient(host);
        using var unrelated = CreateClient(host);
        using var delivery = CreateClient(host);
        var shop = await CreateShopAsync(merchant);
        var otherShop = await CreateShopAsync(unrelated);
        var grant = await BindAsync(shop);
        await DeliverAsync(delivery, key, grant);
        await EnforceAsync(shop);
        var sale = await CreateSaleAsync(merchant);
        var otherSale = await CreateSaleAsync(unrelated);
        var detailPath = $"/api/sales/{sale:D}";
        using var original = await merchant.GetAsync(detailPath, Ct);
        var frozen = (await ReadSuccessAsync(original)).GetProperty("document").GetRawText();

        _clock.Now = grant.Shop!.AccessEndsAt!.Value;
        await AssertHistoryAsync(merchant, detailPath, frozen);

        var trading = grant with
        {
            EventId = Guid.NewGuid(),
            Account = grant.Account! with { Revision = 2, Restriction = "Trading" },
        };
        await DeliverAsync(delivery, key, trading);
        await AssertHistoryAsync(merchant, detailPath, frozen);

        var security = trading with
        {
            EventId = Guid.NewGuid(),
            Account = trading.Account with { Revision = 3, Restriction = "Security" },
        };
        await DeliverAsync(delivery, key, security);
        foreach (var path in new[] { detailPath, "/api/sales", "/api/sales/profit-loss", "/api/exports/sales", "/api/exports/profit-loss" })
            await AssertSecurityDenialAsync(merchant, path);

        using var selector = await merchant.GetAsync("/api/shops/me", Ct);
        Assert.Equal(HttpStatusCode.OK, selector.StatusCode);
        using var otherHistory = await unrelated.GetAsync($"/api/sales/{otherSale:D}", Ct);
        Assert.Equal(HttpStatusCode.OK, otherHistory.StatusCode);
        using var otherTarget = await unrelated.GetAsync($"/api/shops/{shop:D}", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, otherTarget.StatusCode);
        var nonmember = await otherTarget.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Shop.MembershipNotFound", nonmember.GetProperty("title").GetString());
        Assert.Equal("You do not have access to this shop.", nonmember.GetProperty("detail").GetString());

        await DeliverAsync(delivery, key, security with
        {
            EventId = Guid.NewGuid(),
            Account = security.Account with { Revision = 4, Restriction = "Unrestricted", ReleasedRevision = 3 },
        });
        await AssertHistoryAsync(merchant, detailPath, frozen);
    }

    [Fact]
    public async Task Shop_restriction_respects_its_effective_instant_and_enrollment()
    {
        using var key = RSA.Create(2048);
        using var host = CreateHost(key);
        await host.InitializeAsync();
        using var first = CreateClient(host);
        using var sibling = CreateClient(host);
        using var delivery = CreateClient(host);
        var shop = await CreateShopAsync(first);
        var siblingShop = await CreateShopAsync(sibling);
        var firstGrant = await BindAsync(shop);
        var siblingGrant = await BindAsync(siblingShop, firstGrant.AccountId);
        await DeliverAsync(delivery, key, firstGrant);
        await DeliverAsync(delivery, key, siblingGrant);
        await EnforceAsync(siblingShop);
        var sale = await CreateSaleAsync(first);
        var siblingSale = await CreateSaleAsync(sibling);

        var effective = _clock.Now.AddSeconds(2);
        var restriction = firstGrant with
        {
            EventId = Guid.NewGuid(),
            Shop = firstGrant.Shop! with { Revision = 2, Suspension = new CustomerSuspensionV1("Security", effective, "Fixture restriction") },
        };
        await DeliverAsync(delivery, key, restriction);
        _clock.Now = effective;
        using var observeOnly = await first.GetAsync($"/api/sales/{sale:D}", Ct);
        Assert.Equal(HttpStatusCode.OK, observeOnly.StatusCode);

        await EnforceAsync(shop);
        await AssertSecurityDenialAsync(first, $"/api/sales/{sale:D}");
        using var siblingHistory = await sibling.GetAsync($"/api/sales/{siblingSale:D}", Ct);
        Assert.Equal(HttpStatusCode.OK, siblingHistory.StatusCode);
    }

    private static async Task AssertHistoryAsync(HttpClient merchant, string detailPath, string frozen)
    {
        using var detail = await merchant.GetAsync(detailPath, Ct);
        Assert.Equal(frozen, (await ReadSuccessAsync(detail)).GetProperty("document").GetRawText());
        foreach (var path in new[] { "/api/sales", "/api/sales/profit-loss" })
        {
            using var history = await merchant.GetAsync(path, Ct);
            Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        }

        foreach (var (path, signature) in new[]
        {
            ("/api/exports/sales?format=xlsx", "PK"),
            ("/api/exports/sales?format=pdf", "%PDF"),
            ("/api/exports/sales?format=tallyXml", "<"),
            ("/api/exports/profit-loss", "PK"),
        })
        {
            using var export = await merchant.GetAsync(path, Ct);
            Assert.True(export.IsSuccessStatusCode, export.StatusCode.ToString());
            Assert.NotNull(export.Content.Headers.ContentDisposition);
            var bytes = await export.Content.ReadAsByteArrayAsync(Ct);
            Assert.StartsWith(signature, System.Text.Encoding.ASCII.GetString(bytes, 0, signature.Length), StringComparison.Ordinal);
        }
    }

    private static async Task AssertSecurityDenialAsync(HttpClient merchant, string path)
    {
        using var refused = await merchant.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.True(refused.Headers.CacheControl?.NoStore);
        Assert.Null(refused.Content.Headers.ContentDisposition);
        var body = await refused.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Licence.SecuritySuspended", body.GetProperty("code").GetString());
    }

    private sealed class PauseResult(string action, IActionResult? result = null) : IAsyncActionFilter
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var executed = await next();
            if (context.ActionDescriptor.RouteValues["action"] != action || executed.Exception is not null)
                return;

            if (result is not null)
                executed.Result = result;

            Ready.TrySetResult();
            await Resume.Task.WaitAsync(context.HttpContext.RequestAborted);
        }
    }
}
