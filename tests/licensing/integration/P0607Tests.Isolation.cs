using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using static Intelibill.Licensing.Integration.Tests.P0607aTests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0607Tests
{
    [Fact]
    public async Task AppliedTransferKeepsOriginalBillsPrivate()
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var doc = await AttachOriginalAsync(s);
        var authClock = s.F.Hosts.Fixture.Clock.Now;
        var confirmation = await s.Source.Payer.Client.GetStringAsync(doc.Confirmation);
        var originalConfirmation = await s.Source.Payer.Client.GetByteArrayAsync(doc.ConfirmationContent);
        var transfer = await ConsentedAsync(s, s.Permit.ValidUntil);
        var (manager, _) = await s.F.Checkout.Trial.Catalogue.ManagerAsync("privacy@example.test");
        using var session = manager;
        var key = await ApproveAsync(s, transfer, manager);
        await DrainAsync(s);
        await AdvanceAsync(s, s.Permit.ValidUntil);
        await AssertStateAsync(manager, AdminPath(s, transfer), key, "Applied");
        s.F.Hosts.Fixture.Clock.Now = authClock;
        Assert.Equal(0L, await RuntimeCountAsync(s, null, "coverage_transfer_bindings", "true"));
        Assert.Equal(0L, await RuntimeCountAsync(s, Guid.NewGuid(), "coverage_transfer_approvals", "true"));
        Assert.Equal(0L, await RuntimeCountAsync(s, s.Destination.Account, "coverage_transfer_approvals", "true"));
        Assert.Equal(1L, await RuntimeCountAsync(s, s.Source.Account, "coverage_transfer_approvals", "true"));
        var named = $"shop_id='{s.North.ShopId}'";
        Assert.Equal(0L, await RuntimeCountAsync(s, s.Source.Account, "current_shop_affiliations", named));
        Assert.Equal(1L, await RuntimeCountAsync(s, s.Destination.Account, "current_shop_affiliations", named));
        await VerifyDocumentsAsync(s, doc);
        Assert.Equal(originalConfirmation, await s.Source.Payer.Client.GetByteArrayAsync(doc.ConfirmationContent));
        Assert.Equal(confirmation, await s.Source.Payer.Client.GetStringAsync(doc.Confirmation));
    }

    internal sealed record OriginalDocument(Guid Order, Guid Version, string List, string Content, string Confirmation, string ConfirmationContent);

    internal static async Task<OriginalDocument> AttachOriginalAsync(Scenario s)
    {
        var order = (Guid)(await s.F.ScalarAsync($"SELECT order_id FROM payment_fulfilments WHERE term_id='{s.Term}'"))!;
        using var finance = await P0611Tests.SignInBillingAsync(s.F.Checkout);
        var path = $"accounts/{s.Source.Account}/orders/{order}";
        var bytes = P0611Tests.Pdf();
        var upload = await P0611Tests.AttachAsync(finance, $"/api/admin/{path}/billing-documents", bytes);
        var version = upload.GetProperty("versionId").GetGuid();
        var list = $"/api/billing/{path}/billing-documents";
        using var download = await s.Source.Payer.Client.GetAsync($"{list}/{version}/content");
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        var confirmations = $"/api/billing/{path}/payment/confirmations";
        var rows = await s.Source.Payer.Client.GetFromJsonAsync<JsonElement>(confirmations);
        var issued = Assert.Single(rows.EnumerateArray()).GetProperty("confirmationId").GetGuid();
        return new(order, version, list, $"{list}/{version}/content", confirmations, $"{confirmations}/{issued}/document");
    }

    internal static async Task VerifyDocumentsAsync(Scenario s, OriginalDocument doc)
    {
        using var original = await s.Source.Payer.Client.GetAsync(doc.Content);
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        Assert.True(original.Headers.CacheControl?.NoStore);
        Assert.Equal(P0611Tests.Pdf(), await original.Content.ReadAsByteArrayAsync());
        foreach (var path in new[] { doc.List, doc.Content, doc.Confirmation, doc.ConfirmationContent })
        {
            using var foreign = await s.Destination.Payer.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
            using var guessed = await s.Destination.Payer.Client.GetAsync(path.Replace(
                s.Source.Account.ToString("D"), s.Destination.Account.ToString("D"), StringComparison.Ordinal));
            Assert.Equal(HttpStatusCode.NotFound, guessed.StatusCode);
            Assert.DoesNotContain(P0611Tests.Reference, await guessed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }
}
