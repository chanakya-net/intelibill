using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0308Tests
{
    [Fact]
    public async Task RenewedImportedPackageSupportsCancellationAndWithdrawal()
    {
        var importedStart = Utc("2027-01-31T04:30:00Z");
        var importedEnd = Utc("2027-02-28T04:30:00Z");
        await using var fixture = await StartAsync(database, importedEnd.AddDays(-2));
        var payer = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        var second = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await P0209Fixture.CreateAccountAsync(payer.Client);
        var imported = await P0209Fixture.SeedTermAsync(fixture.Hosts, account, new(importedStart, importedEnd,
            AnchorLocal: "2027-01-31 10:00:00", Grouped: true, Capacity: 3));
        foreach (var shop in new[] { payer, second })
            await P0209Fixture.AffiliateAsync(fixture.Hosts, account, payer.UserId, shop.ShopId, shop.UserId,
                imported, importedStart, importedEnd);
        var plan = await P0301Tests.PublishPlanAsync(fixture.Checkout.Trial);
        var package = new Package(payer, account, [payer, second], plan);

        var originalPath = P0215Tests.CancellationPath(account, imported);
        var oldPreview = await P0215Fixture.PreviewAsync(payer, originalPath);
        var oldSchedule = P0215Tests.Accept(oldPreview, Guid.NewGuid());
        var original = await PostCancellationAsync(package, originalPath, oldSchedule);
        var originalWithdrawalPath = P0216Tests.WithdrawalPath(account, imported);
        var oldWithdrawal = P0216Tests.Withdraw(original.GetProperty("cancellation"), Guid.NewGuid());
        var withdrawn = await PostCancellationAsync(package, originalWithdrawalPath, oldWithdrawal);
        var stalePreview = await P0215Fixture.PreviewAsync(payer, originalPath);

        var (_, paid) = await RenewAsync(package, imported, "Month");
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "Applying");
        var successor = paid.GetProperty("termId").GetGuid();
        using var stale = await payer.Client.PostAsJsonAsync(originalPath, P0215Tests.Accept(stalePreview, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Billing.CancellationConflict", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(original.GetRawText(), (await PostCancellationAsync(package, originalPath, oldSchedule)).GetRawText());
        Assert.Equal(withdrawn.GetRawText(), (await PostCancellationAsync(package, originalWithdrawalPath, oldWithdrawal)).GetRawText());

        fixture.At(importedEnd);
        await AssertSuccessorCancellationAsync(fixture, package, successor);
    }

    private static async Task AssertSuccessorCancellationAsync(P0308Fixture fixture, Package package, Guid successor)
    {
        var path = P0215Tests.CancellationPath(package.Account, successor);
        var preview = await P0215Fixture.PreviewAsync(package.Payer, path);
        Assert.Equal(March31, preview.GetProperty("effectiveAt").GetDateTimeOffset());
        Assert.Equal(package.Shops.Select(shop => shop.ShopId).Order(),
            preview.GetProperty("affectedShops").EnumerateArray().Select(shop => shop.GetProperty("shopId").GetGuid()).Order());
        var accepted = await PostCancellationAsync(package, path, P0215Tests.Accept(preview, Guid.NewGuid()));

        fixture.At(March31);
        foreach (var shop in package.Shops)
        {
            var expired = await ShopTermAsync(package, shop);
            Assert.Equal("Expired", expired.GetProperty("state").GetString());
            Assert.Null(expired.GetProperty("graceEndsAt").Deserialize<DateTimeOffset?>());
        }

        fixture.At(March31.AddMicroseconds(-1));
        await PostCancellationAsync(package, P0216Tests.WithdrawalPath(package.Account, successor),
            P0216Tests.Withdraw(accepted.GetProperty("cancellation"), Guid.NewGuid()));
        fixture.At(March31);
        foreach (var shop in package.Shops)
        {
            var grace = await ShopTermAsync(package, shop);
            Assert.Equal("Grace", grace.GetProperty("state").GetString());
            Assert.Equal(March31.AddDays(7), grace.GetProperty("graceEndsAt").GetDateTimeOffset());
        }
        var quote = await RenewalQuoteAsync(package, successor, "Month");
        Assert.Equal("PriorPaidThrough", quote.StartRule);
        Assert.Equal(March31, quote.PreviewStartsAt);
    }

    private static async Task<JsonElement> PostCancellationAsync(Package package, string path, object command)
    {
        using var response = await package.Payer.Client.PostAsJsonAsync(path, command);
        return await P0209Fixture.ReadAsync(response);
    }
}
