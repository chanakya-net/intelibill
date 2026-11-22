using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Intelibill.Integration.Tests;

public sealed partial class P0413cLicensingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restricted_reversal_preserves_exact_source_amount_and_procurement_history(bool grouped)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        var group = grouped ? await ReceiveGroupAsync(client, source) : null;
        var correctedLot = group?.Lots[0] ?? source.Receipt;
        var correctedTotal = grouped ? 387.13m : 975.13m;
        await PostAsync(client, $"/api/inventory/receipts/{correctedLot}/cost-corrections", new
        {
            expectedVersion = (await DetailAsync(client, correctedLot)).GetProperty("receiptVersion").GetUInt32(),
            revisedTotalCost = correctedTotal, reason = "Supplier invoice correction",
        });
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(source.Binding, end);
        await EnforceAsync(source.Shop);
        _clock.Now = end;

        if (group is null)
        {
            var (route, body) = await PrepareAsync(client, source, "receipt");
            var result = await PostAsync(client, route, body);
            Assert.Equal(correctedTotal, result.GetProperty("lots")[0].GetProperty("effectiveAmount").GetDecimal());
            return;
        }

        var orderRoute = $"/api/purchase-orders/{group.Order}";
        var original = await GetAsync(client, orderRoute);
        await AssertGroupVersionConflictAsync(client, source, group, original);
        var reversed = await PostAsync(client, GroupRoute(group), await GroupPayloadAsync(client, group));
        AssertExactGroup(reversed, group, correctedTotal, original);
        AssertProcurementUnchanged(original, await GetAsync(client, orderRoute));
    }

    private async Task AssertGroupVersionConflictAsync(HttpClient client, Source source, Group group, JsonElement original)
    {
        var before = await FactsAsync(source.Shop);
        await AssertProblemAsync(await client.PostAsJsonAsync(GroupRoute(group),
            await GroupPayloadAsync(client, group, invalidateLast: true), Ct),
            HttpStatusCode.Conflict, "SupplierReturn.VersionConflict");
        Assert.Equal(before, await FactsAsync(source.Shop));
        Assert.True(JsonElement.DeepEquals(original, await GetAsync(client, $"/api/purchase-orders/{group.Order}")));
        _output.WriteLine("A stale last lot rejected the whole reversal; stock and procurement facts were unchanged.");
    }

    private static async Task<object> GroupPayloadAsync(HttpClient client, Group group, bool invalidateLast = false)
    {
        var lines = await Task.WhenAll(group.Lots.Order().Select(async id => ReversalLine(await DetailAsync(client, id))));
        if (invalidateLast) lines[^1] = lines[^1] with { ExpectedBatchVersion = 0 };
        return new { operationKey = NewKey(), reason = "Reverse incorrect delivery", lines };
    }

    private static void AssertExactGroup(JsonElement reversed, Group group, decimal corrected, JsonElement original)
    {
        var lots = reversed.GetProperty("lots").EnumerateArray().ToArray();
        Assert.Equal(2, lots.Length);
        var correctedLot = lots.Single(x => x.GetProperty("receiptValueId").GetGuid() == group.Lots[0]);
        Assert.Equal(corrected, correctedLot.GetProperty("effectiveAmount").GetDecimal());
        var other = lots.Single(x => x.GetProperty("receiptValueId").GetGuid() == group.Lots[1]);
        var originalOther = original.GetProperty("receipts")[0].GetProperty("lines").EnumerateArray()
            .Single(x => x.GetProperty("receiptValueId").GetGuid() == group.Lots[1]);
        Assert.Equal(originalOther.GetProperty("quantity").GetDecimal() * 100, other.GetProperty("effectiveAmount").GetDecimal());
    }

    private void AssertProcurementUnchanged(JsonElement before, JsonElement after)
    {
        Assert.Equal(before.GetProperty("status").GetString(), after.GetProperty("status").GetString());
        Assert.Equal(10, after.GetProperty("receivedQuantity").GetDecimal());
        Assert.Equal(0, after.GetProperty("netRetainedQuantity").GetDecimal());
        var beforeLine = before.GetProperty("lines")[0];
        var afterLine = after.GetProperty("lines")[0];
        foreach (var field in new[] { "lineId", "itemId", "description", "expectedQuantity", "receivedQuantity", "remainingQuantity", "unitCost", "lineTotal" })
            Assert.True(JsonElement.DeepEquals(beforeLine.GetProperty(field), afterLine.GetProperty(field)), field);
        Assert.Equal(0, afterLine.GetProperty("netRetainedQuantity").GetDecimal());
        _output.WriteLine("Group reversal preserved PO status and lines, with 10 historically received and 0 net retained.");
    }

    private static string GroupRoute(Group group) => $"/api/purchase-order-receipts/{group.Id}/reversal";
}
