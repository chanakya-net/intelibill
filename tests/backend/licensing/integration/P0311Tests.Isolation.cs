using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0311Tests
{
    [Fact]
    public async Task LaterPartyCatalogueProfileAndShopEditsCannotChangeIssuedBytes()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart });
        var purchase = await VerifiedAsync(fixture);
        var path = PaymentPath(purchase.Quote) + "/confirmations";
        var list = await purchase.Payer.Client.GetFromJsonAsync<JsonElement>(path);
        var id = Assert.Single(list.EnumerateArray()).GetProperty("confirmationId").GetGuid();
        var document = path + $"/{id}/document";
        var original = await purchase.Payer.Client.GetByteArrayAsync(document);
        await MutateAsync(fixture, purchase);
        Assert.Equal(original, await purchase.Payer.Client.GetByteArrayAsync(document));
        Assert.Equal(list.GetRawText(), (await purchase.Payer.Client.GetFromJsonAsync<JsonElement>(path)).GetRawText());
        foreach (var sql in new[] { "UPDATE public.payment_confirmations SET facts = '{}'::jsonb", "DELETE FROM public.payment_confirmations", "TRUNCATE public.payment_confirmations" })
        {
            var refused = await Assert.ThrowsAsync<PostgresException>(() => LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, sql));
            Assert.Equal("55000", refused.SqlState);
        }
    }

    [Fact]
    public async Task ForeignAccountNonMemberAndUnknownIdsAreRefused()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart });
        var purchase = await VerifiedAsync(fixture);
        var path = PaymentPath(purchase.Quote) + "/confirmations";
        var list = await purchase.Payer.Client.GetFromJsonAsync<JsonElement>(path);
        var id = list[0].GetProperty("confirmationId").GetGuid();
        var outsider = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        using var foreign = await outsider.Client.GetAsync(path + $"/{id}/document");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        var other = await P0209Fixture.CreateAccountAsync(purchase.Payer.Client);
        using var wrong = await purchase.Payer.Client.GetAsync(path.Replace(purchase.Account.ToString("D"), other.ToString("D"), StringComparison.Ordinal) + $"/{id}/document");
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        using var unknown = await purchase.Payer.Client.GetAsync(path + $"/{Guid.NewGuid()}/document");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("Billing.PaymentConfirmationNotFound", (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    internal static async Task<Purchase> VerifiedAsync(P0302Fixture fixture)
    {
        var purchase = await fixture.AcceptedQuoteAsync(1);
        var attempt = (await StartAsync(purchase.Payer.Client, purchase.Quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        await CaptureAsync(purchase.Payer.Client, purchase.Quote, attempt);
        await VerifyAsync(purchase.Payer.Client, purchase.Quote, attempt);
        return purchase;
    }

    internal static async Task MutateAsync(P0302Fixture fixture, Purchase purchase)
    {
        await TransferOwnershipAsync(fixture, purchase);
        await P0301Tests.RetirePlanAsync(fixture.Trial, purchase.Quote);
        await P0301Tests.PublishPlanAsync(fixture.Trial, monthlyPricePaise: 99000);
        await LicensingPostgreSqlFixture.ExecuteAsync(new NpgsqlConnectionStringBuilder(fixture.Delivery.MerchantDb.ConnectionString),
            $"UPDATE public.shops SET name = 'Renamed after issuance' WHERE id = '{purchase.Payer.ShopId:D}'; UPDATE public.users SET first_name = 'Changed profile' WHERE id = '{purchase.Payer.UserId:D}'");
        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
    }
    private static async Task TransferOwnershipAsync(P0302Fixture fixture, Purchase purchase)
    {
        var named = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        using var issued = await purchase.Payer.Client.PostAsJsonAsync($"/api/billing/accounts/{purchase.Account}/ownership-transfers",
            new { operationKey = Guid.NewGuid(), expectedVersion = 1, recipientKind = "email", recipient = named.Email,
                reason = "Fixture responsibility handover after issuance", confirmTransfer = true });
        Assert.Equal(HttpStatusCode.Accepted, issued.StatusCode);
        var inbox = await named.Client.GetFromJsonAsync<JsonElement>("/api/billing/ownership-transfers");
        var transfer = Assert.Single(inbox.GetProperty("transfers").EnumerateArray()).GetProperty("transferId").GetGuid();
        using var accepted = await named.Client.PostAsJsonAsync($"/api/billing/ownership-transfers/{transfer}/accept",
            new { operationKey = Guid.NewGuid(), expectedVersion = 1, responsibilityPolicyVersion = "prospective-billing-owner/1", acceptResponsibility = true });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

}
