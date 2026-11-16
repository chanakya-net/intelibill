using System.Net;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0211Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtendedTrialKeepsItsAccountSlotAndDisclosesCapExceptions(bool overlap)
    {
        await using var fixture = await P0210Fixture.StartAsync(database);
        var (owner, account) = await StartTrialAsync(fixture);
        var branch = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        await fixture.LinkAsync(account, owner.UserId, branch);
        var manager = fixture.CatalogueManager!;
        var path = Path(account, owner.ShopId);
        var preview = await PreviewAsync(manager, path);
        Assert.Equal(1, preview.GetProperty("accountTrialCap").GetInt32());
        Assert.False(preview.GetProperty("accountTrialCapReached").GetBoolean());
        fixture.Hosts.StatusClock.Now = P0210Fixture.Start.AddDays(14);

        if (overlap)
        {
            Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(branch, fixture.Accept())).Status);
            using var stale = await P0212Fixture.PostAsync(manager, path, Confirm(preview, Guid.NewGuid()));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("TrialExtension.VersionConflict", await P0206Fixture.CodeAsync(stale));
            var exception = await ApproveAsync(fixture, account, owner.ShopId);
            Assert.True(exception.GetProperty("accountTrialCapReached").GetBoolean());
            return;
        }

        await ApproveAsync(fixture, account, owner.ShopId);
        var held = await P0210Tests.StartAndWaitAsync(branch, fixture.Accept());
        Assert.Equal("NeedsReview", held.Status);
        Assert.Equal("AccountCap", held.Reason);
        fixture.Hosts.StatusClock.Now = End;
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(branch, fixture.Accept())).Status);
    }
}
