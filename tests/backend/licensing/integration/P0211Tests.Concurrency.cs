using System.Net;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0211Tests
{
    [Fact]
    public async Task StalePreviewsAndChangedPayloadsConflict()
    {
        await using var fixture = await P0210Fixture.StartAsync(database);
        var (owner, account) = await StartTrialAsync(fixture);
        var manager = fixture.CatalogueManager!;
        var path = Path(account, owner.ShopId);
        var command = Confirm(await PreviewAsync(manager, path), Guid.NewGuid());
        using (var changed = await P0212Fixture.PostAsync(manager, path, command with { Reason = "Changed explanation" }))
            Assert.Equal("TrialExtension.VersionConflict", await P0206Fixture.CodeAsync(changed));
        using (var written = await P0212Fixture.PostAsync(manager, path, command))
            Assert.Equal(HttpStatusCode.Created, written.StatusCode);
        using (var different = await P0212Fixture.PostAsync(manager, path, command with { ReviewReference = "EVAL-858" }))
            Assert.Equal("TrialExtension.PayloadMismatch", await P0206Fixture.CodeAsync(different));
        using (var stale = await P0212Fixture.PostAsync(manager, path, command with { OperationKey = Guid.NewGuid() }))
            Assert.Equal("TrialExtension.VersionConflict", await P0206Fixture.CodeAsync(stale));

        var next = await ApproveAsync(fixture, account, owner.ShopId, End.AddDays(1));
        Assert.Equal(2, next.GetProperty("extendedVersion").GetInt64());
        Assert.Equal(End, next.GetProperty("previousEndsAt").GetDateTimeOffset());
        fixture.Hosts.StatusClock.Now = End.AddDays(2);
        using var replay = await P0212Fixture.PostAsync(manager, path, command);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(End, (await P0206Fixture.BodyAsync(replay)).GetProperty("endsAt").GetDateTimeOffset());
        Assert.Equal(2L, await fixture.CountAsync("trial_extension_receipts"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompetingApprovalsCommitExactlyOneExtensionPerVersion(bool sameKey)
    {
        await using var fixture = await P0210Fixture.StartAsync(database);
        var (owner, account) = await StartTrialAsync(fixture);
        var path = Path(account, owner.ShopId);
        var manager = fixture.CatalogueManager!;
        using var second = await fixture.Catalogue.SignInOperatorAsync(fixture.CatalogueManagerSeed!);
        var command = Confirm(await PreviewAsync(manager, path), Guid.NewGuid());
        var competing = sameKey ? command : command with { OperationKey = Guid.NewGuid() };
        var fences = new P0212Fixture(fixture.Catalogue, fixture.Hosts.StatusClock);
        await using var fence = await P0212Tests.HoldAccountFenceAsync(fences, account);
        var firstWrite = P0212Fixture.PostAsync(manager, path, command);
        var secondWrite = P0212Fixture.PostAsync(second, path, competing);
        try
        {
            await P0212Tests.WaitForAdvisoryWaitersAsync(fences, fence.Connection.ProcessID, 2);
        }
        finally
        {
            await fence.Transaction.CommitAsync();
        }

        using var first = await firstWrite;
        using var last = await secondWrite;
        Assert.Contains(HttpStatusCode.Created, new[] { first.StatusCode, last.StatusCode });
        var duplicate = first.StatusCode == HttpStatusCode.Created ? last : first;
        Assert.Equal(sameKey ? HttpStatusCode.OK : HttpStatusCode.Conflict, duplicate.StatusCode);
        if (sameKey) Assert.Equal(await first.Content.ReadAsStringAsync(), await last.Content.ReadAsStringAsync());
        else Assert.Equal("TrialExtension.VersionConflict", await P0206Fixture.CodeAsync(duplicate));
        Assert.Equal(1L, await fixture.CountAsync("trial_extension_receipts"));
    }
}
