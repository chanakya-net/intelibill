using System.Net;
using static Intelibill.Licensing.Integration.Tests.P0609Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0610Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task ApprovalCreatesExactlyOneDiscoverableOnlineAttempt()
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        await P0608Tests.DrainAsync(fixture);
        var snapshots = await fixture.CountAsync("entitlement_snapshots");
        var at = Instant.AddDays(10);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var preview = await PreviewAsync(billing, package.Account, request, 4500, "EndAt", at);
        var command = Command(preview, 4500, "EndAt", at);
        var receipt = await ReadAsync(await ApproveAsync(billing, package.Account, request, command), HttpStatusCode.Created);
        Assert.Equal(snapshots, await fixture.CountAsync("entitlement_snapshots"));
        var term = (await P0209Fixture.ReadAsync(await P0209Fixture.ShopStatusAsync(
            package.Shops[0].Client, package.Shops[0].ShopId))).GetProperty("term");
        Assert.Equal(preview.GetProperty("access").GetProperty("previousPaidThrough").GetDateTimeOffset(),
            term.GetProperty("paidThrough").GetDateTimeOffset());
        var replay = await ReadAsync(await ApproveAsync(billing, package.Account, request, command), HttpStatusCode.OK);
        Assert.Equal(receipt.GetProperty("reservationId").GetGuid(), replay.GetProperty("reservationId").GetGuid());
        Assert.Equal(1L, await fixture.CountAsync("refund_attempts", "amount_paise = 4500"));
    }
}
