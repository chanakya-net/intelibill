using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0607aTests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0607Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task ReviewShowsSeparatePeriodsAndExactAllocation()
    {
        using var key = RSA.Create(2048);
        await using var s = await StartAsync(database, key);
        var cutover = s.Permit.ValidUntil.AddMinutes(1);
        var id = await ConsentedAsync(s, cutover);
        var participant = await ReadAsync(s.Source.Payer.Client, s.Source.Account, id);
        Assert.True(participant.GetProperty("consentsCollected").GetBoolean());
        var before = await s.BaselineAsync();

        var (manager, _) = await s.F.Checkout.Trial.Catalogue.ManagerAsync("transfer.manager@example.test");
        using var session = manager;
        using var preview = await manager.SendAsync(HttpMethod.Post,
            $"/api/admin/accounts/{s.Source.Account}/coverage-transfers/{id}/preview", new { },
            await P0206Fixture.ProofAsync(manager));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var view = await P0206Fixture.BodyAsync(preview);
        Assert.Equal("North", view.GetProperty("shopName").GetString());
        Assert.Equal(s.Source.Account, view.GetProperty("sourceAccountId").GetGuid());
        Assert.Equal(s.Destination.Account, view.GetProperty("destinationAccountId").GetGuid());
        Assert.Equal(s.Term, view.GetProperty("sourceTermId").GetGuid());
        Assert.Equal(cutover, view.GetProperty("cutoverAt").GetDateTimeOffset());
        Assert.Equal(P0601Tests.February29, view.GetProperty("sourcePaidThroughAt").GetDateTimeOffset());
        var term = Assert.Single(view.GetProperty("destinationTerms").EnumerateArray());
        Assert.Equal(s.DestinationTerm, term.GetProperty("termId").GetGuid());
        Assert.NotEqual(P0601Tests.February29, term.GetProperty("paidThroughAt").GetDateTimeOffset());
        Assert.Equal(before, await s.BaselineAsync());
    }

    private static async Task<Guid> ConsentedAsync(Scenario s, DateTimeOffset? cutover)
    {
        var path = RequestPath(s.Source.Account, s.Term);
        using var preview = await s.Source.Payer.Client.PostAsJsonAsync(path + "/preview",
            s.PreviewBody(cutover is null ? "RetainUntilPaidThrough" : "ReviewedAllocation", cutover));
        preview.EnsureSuccessStatusCode();
        using var created = await s.Source.Payer.Client.PostAsJsonAsync(path,
            RequestBody(await preview.Content.ReadFromJsonAsync<JsonElement>(), Guid.NewGuid()));
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transferId").GetGuid();
        await AcceptAsync(s.Source.Payer.Client, s.Source.Account, id, "source-billing-owner");
        await AcceptAsync(s.Destination.Payer.Client, s.Destination.Account, id, "destination-billing-owner");
        await ShopAcceptAsync(s, id);
        return id;
    }
}
