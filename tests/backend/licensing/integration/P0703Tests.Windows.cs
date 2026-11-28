using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0703Tests
{
    [Theory]
    [InlineData(-1, -10, "PaidMinus7Days")]
    [InlineData(-1, 0, "PaidMinus1Day")]
    [InlineData(-1, 10, "PaidMinus1Day")]
    [InlineData(0, -10, "PaidMinus1Day")]
    [InlineData(0, 0, "GraceStart")]
    [InlineData(0, 10, "GraceStart")]
    [InlineData(7, 0, "GraceExpiry")]
    [InlineData(12, 0, "GraceExpiry")]
    public async Task DeliversOnlyTheCurrentThresholdAndGuidance(int days, int ticks, string expected)
    {
        await using var paid = await P0308Fixture.StartAsync(database, Start, Settings(mailpit));
        var package = await paid.PackageAsync(1);
        await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        paid.At(Due.AddTicks(-10));
        Assert.False(await DeliverAsync(paid));
        paid.At(Start.AddMonths(1).AddDays(days).AddTicks(ticks));
        await DrainMailAsync(paid);
        Assert.Equal("SmtpAccepted", await StateAsync(paid, package, expected));
        var body = Assert.Single(await mailpit.ReadAsync(package.Payer.Email));
        var deadline = expected.StartsWith("Grace", StringComparison.Ordinal) ? Start.AddMonths(1).AddDays(7) : Start.AddMonths(1);
        Assert.Contains($"IST ({deadline:O} UTC)", body);
        if (ticks != 0)
            Assert.DoesNotContain("before paid term ends", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeliversOnlyTheCurrentThresholdAndGuidanceForShortenedPaidTerm()
    {
        await using var paid = await P0609Tests.StartAsync(database, Start, Settings(mailpit));
        var package = await paid.PackageAsync(1);
        var (order, _) = await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var end = Start.AddDays(2);
        await P0701Tests.ApproveShortenedRefundAsync(paid, package, order.OrderId, end);
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        paid.At(end.AddDays(-1));
        await DrainMailAsync(paid);
        Assert.Contains($"IST ({end:O} UTC)", Assert.Single(await mailpit.ReadAsync(package.Payer.Email)));
        paid.At(Due);
        await DrainMailAsync(paid);
        Assert.Equal("Suppressed", await StateAsync(paid, package));
        Assert.Single(await mailpit.ReadAsync(package.Payer.Email));
    }

    [Fact]
    public async Task DeliversOnlyTheCurrentThresholdAndGuidanceForExtendedSuspendedTrial()
    {
        await using var paid = await P0308Fixture.StartAsync(database, Start, Settings(mailpit));
        var package = await paid.PackageAsync(1);
        var trial = paid.Checkout.Trial;
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(package.Payer, trial.Accept())).Status);
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var end = Start.AddDays(20).AddMinutes(13);
        await P0701Tests.ExtendTrialAsync(trial.CatalogueManager!, package.Account, package.Payer.ShopId, end);
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        using var owner = await trial.Catalogue.SignInOwnerAsync();
        using var security = await P0701Tests.SignInAdminWithAsync(
            trial.Catalogue,
            owner,
            "p0703-suspension@example.test",
            "security-administrator");
        await P0404Tests.SuspendAsync(security, $"/api/admin/accounts/{package.Account:D}/restrictions", "Account", null);
        paid.At(end.AddDays(-1));
        await DrainMailAsync(paid);
        var body = Assert.Single(await mailpit.ReadAsync(package.Payer.Email));
        Assert.Contains($"IST ({end:O} UTC)", body);
        Assert.Contains("Payment alone does not lift a suspension", body);
        Assert.DoesNotContain("Start a paid plan", body);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeliversOnlyTheCurrentThresholdAndGuidanceOnSuspension(bool allShops)
    {
        await using var paid = await P0308Fixture.StartAsync(database, Start, Settings(mailpit));
        var package = await paid.PackageAsync(2);
        await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        using var owner = await paid.Checkout.Trial.Catalogue.SignInOwnerAsync();
        using var security = await P0701Tests.SignInAdminWithAsync(
            paid.Checkout.Trial.Catalogue, owner, "p0703-suspend-paid@example.test", "security-administrator");
        await P0404Tests.SuspendAsync(security, $"/api/admin/accounts/{package.Account:D}/restrictions",
            allShops ? "Account" : "Shop", allShops ? null : package.Shops[1].ShopId);
        paid.At(Due);
        await DrainMailAsync(paid);
        Assert.Equal(allShops ? "Suppressed" : "SmtpAccepted", await StateAsync(paid, package));
        Assert.Empty(await mailpit.ReadAsync(package.Shops[1].Email));
        var messages = await mailpit.ReadAsync(package.Payer.Email);
        if (allShops)
            Assert.Empty(messages);
        else
            Assert.Contains("renewing does not lift a suspension", Assert.Single(messages));
        var reason = await paid.ScalarAsync($"""
            SELECT e.reason FROM notification_email_events e JOIN notification_email_deliveries d ON d.id = e.delivery_id
                JOIN notification_jobs j ON j.id = d.job_id
            WHERE j.scope_id = '{package.Shops[1].ShopId:D}' AND j.threshold = 'PaidMinus7Days' AND e.state = 'Suppressed'
            """);
        Assert.Equal("RestrictionChanged", reason);
    }
}
