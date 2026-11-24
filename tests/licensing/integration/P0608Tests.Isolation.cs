using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0608Tests
{
    [Fact]
    public async Task ListsAreAccountScoped()
    {
        await using var fixture = await StartAsync(database, RequestedInstant);
        var package = await fixture.PackageAsync(1, ["Scoped Shop"]);
        var (purchase, _) = await PurchaseAsync(package, "Month");
        var confirmation = await ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);
        var otherAccount = await P0209Fixture.CreateAccountAsync(package.Payer.Client, "Empty Other Account");
        var stranger = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);

        // Create 26 refund requests to cross PageSize (25).
        var createdIds = new List<Guid>(26);
        for (var i = 0; i < 26; i++)
        {
            fixture.At(RequestedInstant.AddSeconds(i));
            using var post = await PostRefundAsync(package.Payer.Client, package.Account,
                RequestBody(purchase.OrderId, confirmation, 100L + i, $"Paged refund request #{i + 1}"));
            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
            using var doc = JsonDocument.Parse(await post.Content.ReadAsStringAsync());
            createdIds.Add(doc.RootElement.GetProperty("refundRequestId").GetGuid());
        }

        // Page 1 returns 25 items + nextCursor; page 2 returns the 26th item + null nextCursor.
        using var firstResponse = await package.Payer.Client.GetAsync(RefundsPath(package.Account));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.True(firstResponse.Headers.CacheControl?.NoStore);
        var first = (await firstResponse.Content.ReadFromJsonAsync<RefundRequestPageV1>())!;
        Assert.Equal(25, first.Items.Length);
        Assert.Equal(first.Items[^1].RefundRequestId, first.NextCursor);

        using var secondResponse = await package.Payer.Client.GetAsync($"{RefundsPath(package.Account)}?after={first.NextCursor:D}");
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var second = (await secondResponse.Content.ReadFromJsonAsync<RefundRequestPageV1>())!;
        Assert.Single(second.Items);
        Assert.Null(second.NextCursor);

        var combined = first.Items.Concat(second.Items).Select(item => item.RefundRequestId).ToArray();
        Assert.Equal(createdIds, combined);

        // Foreign / unknown cursor returns empty page.
        var foreignCursor = (await package.Payer.Client.GetFromJsonAsync<RefundRequestPageV1>(
            $"{RefundsPath(package.Account)}?after={Guid.NewGuid():D}"))!;
        Assert.Empty(foreignCursor.Items);
        Assert.Null(foreignCursor.NextCursor);

        // Second account of same payer sees empty page; stranger gets 403.
        var otherPage = (await package.Payer.Client.GetFromJsonAsync<RefundRequestPageV1>(RefundsPath(otherAccount)))!;
        Assert.Empty(otherPage.Items);
        Assert.Null(otherPage.NextCursor);

        using var forbidden = await stranger.Client.GetAsync(RefundsPath(package.Account));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Runtime role direct read under another account GUC sees zero rows (forced RLS).
        await using var runtime = new NpgsqlConnection(fixture.Hosts.Database.Login(
            fixture.Hosts.Fixture.Owner.Database!, "licensing_runtime").ConnectionString);
        await runtime.OpenAsync();
        await using var tx = await runtime.BeginTransactionAsync();
        await using (var set = new NpgsqlCommand($"SELECT set_config('app.billing_account_id', '{otherAccount:D}', true)", runtime, tx))
            await set.ExecuteNonQueryAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM public.refund_requests", runtime, tx);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task BillingOperatorReadsIntakeAndOthersAreRefused()
    {
        await using var fixture = await StartAsync(database, RequestedInstant);
        var package = await fixture.PackageAsync(1, ["Intake Shop"]);
        var (purchase, _) = await PurchaseAsync(package, "Month");
        var confirmation = await ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);
        using var created = await PostRefundAsync(package.Payer.Client, package.Account,
            RequestBody(purchase.OrderId, confirmation, 5000L, "Billing operator intake check"));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        // Billing operator reads per-account intake with provider pin and actor provenance.
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        using var intakeResponse = await billing.SendAsync(HttpMethod.Get,
            $"/api/admin/accounts/{package.Account:D}/refund-requests");
        var body = await intakeResponse.Content.ReadAsStringAsync();
        Assert.True(intakeResponse.StatusCode == HttpStatusCode.OK, $"{(int)intakeResponse.StatusCode}: {body}");
        Assert.True(intakeResponse.Headers.CacheControl?.NoStore);

        using var doc = JsonDocument.Parse(body);
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(package.Account, item.GetProperty("accountId").GetGuid());
        Assert.Equal(purchase.OrderId, item.GetProperty("orderId").GetGuid());
        Assert.Equal(confirmation, item.GetProperty("confirmationId").GetGuid());
        Assert.Equal(package.Payer.UserId, item.GetProperty("actorId").GetGuid());
        Assert.Equal("Online", item.GetProperty("paymentSource").GetString());
        Assert.Equal("Requested", item.GetProperty("status").GetString());
        Assert.Equal(5000L, item.GetProperty("amountPaise").GetInt64());
        Assert.Equal(12000L, item.GetProperty("paymentAmountPaise").GetInt64());
        Assert.Equal("INR", item.GetProperty("currency").GetString());
        Assert.Equal("Billing operator intake check", item.GetProperty("reason").GetString());
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("providerId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("providerAccountId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("environment").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("configurationVersion").GetString()));

        // Operator without the billing capability is refused with 403.
        using var owner = await fixture.Checkout.Trial.Catalogue.SignInOwnerAsync();
        const string supportEmail = "support.norefund@example.test";
        var supportSeed = await fixture.Checkout.Trial.Catalogue.EnrollOperatorAsync(owner, supportEmail);
        await P0206Fixture.GrantAsync(owner, supportEmail, "support");
        using var support = await fixture.Checkout.Trial.Catalogue.SignInOperatorAsync(supportSeed, supportEmail);
        using var denied = await support.SendAsync(HttpMethod.Get,
            $"/api/admin/accounts/{package.Account:D}/refund-requests");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
