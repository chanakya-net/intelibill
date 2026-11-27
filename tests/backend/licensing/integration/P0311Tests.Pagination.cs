using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0311Tests
{
    [Fact]
    public async Task MerchantTraversesAllHundredFullManualReceiptsWithinRelayBound()
    {
        var clock = new P0201Clock { Now = WholeSecondStart };
        await using var fixture = await P0302Fixture.StartAsync(database, clock, P0306Tests.Settings);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        using var billing = await P0306Tests.SignInBillingAsync(fixture);
        for (var i = 0; i < 100; i++)
        {
            clock.Now = WholeSecondStart.AddSeconds(i / 2);
            using var receipt = await P0212Fixture.PostAsync(billing, P0306Tests.Path(purchase.Quote),
                P0306Tests.Receipt(purchase.Quote, clock.Now));
            Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        }
        Assert.Equal(100L, await fixture.CountAsync("payment_confirmations"));
        var path = PaymentPath(purchase.Quote) + "/confirmations";
        var rows = await ReadAllConfirmationPagesAsync(purchase.Payer.Client, path);
        Assert.Equal(100, rows.Length);
        Assert.Equal(100, rows.Select(row => row.ConfirmationId).Distinct().Count());
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(rows, JsonSerializerOptions.Web).Length > 16384);
        Assert.Equal(WholeSecondStart, rows[0].VerifiedAt);
        Assert.Equal(clock.Now, rows[^1].VerifiedAt);
        foreach (var row in new[] { rows[0], rows[^1] })
        {
            using var document = await purchase.Payer.Client.GetAsync(path + $"/{row.ConfirmationId}/document");
            Assert.Equal(HttpStatusCode.OK, document.StatusCode);
            Assert.Equal("attachment", document.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Contains(row.ConfirmationId.ToString("D"), await document.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ConfirmationCursorCannotCrossAccountOrOrderScope()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart });
        var first = await VerifiedAsync(fixture);
        var other = await VerifiedAsync(fixture);
        var path = PaymentPath(first.Quote) + "/confirmations";
        var row = Assert.Single((await first.Payer.Client.GetFromJsonAsync<PaymentConfirmationV1[]>(path))!);
        var foreignPath = PaymentPath(other.Quote) + $"/confirmations/after/{row.ConfirmationId}";
        Assert.Empty((await other.Payer.Client.GetFromJsonAsync<PaymentConfirmationV1[]>(foreignPath))!);
        using var forbidden = await first.Payer.Client.GetAsync(foreignPath);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Empty((await first.Payer.Client.GetFromJsonAsync<PaymentConfirmationV1[]>(path + $"/after/{Guid.NewGuid()}"))!);
    }

    private static async Task<PaymentConfirmationV1[]> ReadAllConfirmationPagesAsync(HttpClient client, string path)
    {
        var all = new List<PaymentConfirmationV1>();
        for (var page = 0; page < 5; page++)
        {
            using var response = await client.GetAsync(path + (all.Count == 0 ? "" : $"/after/{all[^1].ConfirmationId}"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.InRange(bytes.Length, 2, PaymentConfirmationsV1.MaxListBytes);
            var rows = JsonSerializer.Deserialize<PaymentConfirmationV1[]>(bytes, JsonSerializerOptions.Web)!;
            Assert.InRange(rows.Length, 0, 25);
            all.AddRange(rows);
            if (rows.Length < 25) return all.ToArray();
        }
        Assert.Fail("Pagination did not terminate within the expected five pages.");
        return [];
    }
}
