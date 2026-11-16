using System.Net;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0211Tests
{
    [Fact]
    public async Task UnreasonedUnconfirmedOrNonFutureExtensionsWriteNothing()
    {
        await using var fixture = await P0210Fixture.StartAsync(database);
        var (owner, account) = await StartTrialAsync(fixture);
        var manager = fixture.CatalogueManager!;
        var path = Path(account, owner.ShopId);
        var valid = Confirm(await PreviewAsync(manager, path), Guid.NewGuid());
        foreach (var invalid in new[]
        {
            valid with { Reason = " " },
            valid with { ReviewReference = "" },
            valid with { Confirmed = false },
            valid with { EndsAt = "2027-03-10T13:15:30" },
            valid with { EndsAt = "2027-03-10T13:15:30.1234567Z" },
        })
        {
            using var response = await P0212Fixture.PostAsync(manager, path, invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("TrialExtension.Invalid", await P0206Fixture.CodeAsync(response));
        }

        using (var unknown = await P0212Fixture.PostRawAsync(manager, path + "/preview",
            """{"endsAt":"2027-03-10T13:15:30Z","reason":"Inventory","reviewReference":"EVAL","unexpected":true}"""))
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        using (var before = await P0212Fixture.PostAsync(manager, path, valid with { EndsAt = "2027-02-24T00:00:00.123456Z" }))
            Assert.Equal("TrialExtension.EndNotLater", await P0206Fixture.CodeAsync(before));

        fixture.Hosts.StatusClock.Now = End;
        using var past = await P0212Fixture.PostAsync(manager, path, valid);
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.Equal("TrialExtension.EndNotLater", await P0206Fixture.CodeAsync(past));
        Assert.Equal(0L, await fixture.CountAsync("trial_extensions"));
        Assert.Equal(0L, await fixture.CountAsync("trial_extension_receipts"));
    }
}
