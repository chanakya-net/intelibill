using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0211Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    private static readonly DateTimeOffset End = Utc("2027-03-10T13:15:30.123456Z");
    private const string Reason = "Evaluation delayed by inventory count";
    private const string Review = "EVAL-857";

    [Fact]
    public async Task ManagerPreviewsAndApprovesAnExactTrialEndWithoutChangingTheOriginalTrialOrRevenue()
    {
        await using var fixture = await P0210Fixture.StartAsync(database);
        var (owner, account) = await StartTrialAsync(fixture);
        var manager = fixture.CatalogueManager!;
        var path = Path(account, owner.ShopId);
        var before = await OriginalFactsAsync(fixture, owner.ShopId);
        var preview = await PreviewAsync(manager, path);
        Assert.Equal(Reason, preview.GetProperty("reason").GetString());
        Assert.Equal(Review, preview.GetProperty("reviewReference").GetString());
        Assert.Equal(0, preview.GetProperty("currentVersion").GetInt64());
        Assert.Equal(P0210Fixture.Start.AddDays(14), preview.GetProperty("currentEndsAt").GetDateTimeOffset());
        Assert.Equal(End, preview.GetProperty("endsAt").GetDateTimeOffset());

        var key = Guid.NewGuid();
        var command = Confirm(preview, key);
        using var created = await P0212Fixture.PostAsync(manager, path, command);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var result = await P0206Fixture.BodyAsync(created);
        Assert.False(result.GetProperty("paymentRecorded").GetBoolean());
        var status = await P0212Fixture.ShopStatusAsync(manager, account, owner.ShopId);
        var term = status.GetProperty("term");
        Assert.Equal("Trial", term.GetProperty("basis").GetString());
        Assert.Equal(preview.GetProperty("termId").GetGuid(), term.GetProperty("termId").GetGuid());
        Assert.Equal(End, term.GetProperty("accessEndsAt").GetDateTimeOffset());

        Assert.Equal(before, await OriginalFactsAsync(fixture, owner.ShopId));
        Assert.Equal(0L, await fixture.CountAsync("subscription_term_facts"));
        using var replay = await P0212Fixture.PostAsync(manager, path, command);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(result.GetRawText(), (await P0206Fixture.BodyAsync(replay)).GetRawText());
        using var recovered = await manager.SendAsync(HttpMethod.Get, $"{path}/operations/{key:D}");
        Assert.Equal(result.GetRawText(), (await P0206Fixture.BodyAsync(recovered)).GetRawText());
        var eligibility = await owner.Client.GetFromJsonAsync<ShopTrialPreviewV1>(P0210Tests.Path(owner.ShopId));
        Assert.Equal("AlreadyUsed", eligibility!.Reason);
    }

    private static async Task<(Merchant Owner, Guid Account)> StartTrialAsync(P0210Fixture fixture)
    {
        var owner = await SignInAsync(fixture.Hosts, Intelibill.Domain.Enums.ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        await fixture.LinkAsync(account, owner.UserId, owner);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(owner, fixture.Accept())).Status);
        return (owner, account);
    }

    private static string Path(Guid account, Guid shop) => $"/api/admin/accounts/{account:D}/shops/{shop:D}/trial-extensions";

    private static async Task<JsonElement> PreviewAsync(P0110CookieBrowser manager, string path, DateTimeOffset? end = null)
    {
        using var response = await P0212Fixture.PostAsync(manager, path + "/preview",
            new { endsAt = (end ?? End).ToString("O", System.Globalization.CultureInfo.InvariantCulture), reason = Reason, reviewReference = Review });
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await P0206Fixture.BodyAsync(response);
    }

    private static ExtensionCommand Confirm(JsonElement preview, Guid key) => new(key, preview.GetProperty("endsAt").GetString()!,
        Reason, Review, preview.GetProperty("currentVersion").GetInt64(), preview.GetProperty("sourceHash").GetString()!, true);

    private sealed record ExtensionCommand(Guid OperationKey, string EndsAt, string Reason, string ReviewReference,
        long CurrentVersion, string SourceHash, bool Confirmed);

    internal static async Task<string> OriginalFactsAsync(P0210Fixture fixture, Guid shop) =>
        (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.LicensingOwner, $"""
            SELECT jsonb_build_array(to_jsonb(t), to_jsonb(c),
                (SELECT jsonb_agg(to_jsonb(i)) FROM shop_trial_inbox i WHERE i.shop_id = t.shop_id))::text
            FROM trial_history t JOIN subscription_coverage_terms c ON c.term_id = t.term_id
            WHERE t.shop_id = '{shop:D}'
            """))!;
}
