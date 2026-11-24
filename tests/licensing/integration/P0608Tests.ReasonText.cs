using System.Net;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0608Tests
{
    private const string ByteOrderMarkReason = "﻿Duplicate shop setup";

    /// <summary>The browser consumers read the same vectors, so all three agree on recorded reason text.</summary>
    [Fact]
    public void ReasonNormalizationMatchesSharedContractVectors()
    {
        var contractPath = Path.Combine(ProjectPaths.Root, "tests/contracts/licensing/refund-request-v1.json");
        using var contract = JsonDocument.Parse(File.ReadAllText(contractPath));
        var vectors = contract.RootElement.GetProperty("reasonNormalization").GetProperty("vectors")
            .EnumerateArray().ToArray();

        Assert.NotEmpty(vectors);
        Assert.Contains(vectors, vector => vector.GetProperty("input").GetString() == ByteOrderMarkReason);
        Assert.All(vectors, vector => Assert.Equal(
            vector.GetProperty("normalized").GetString(),
            RefundRequestsV1.NormalizeReason(vector.GetProperty("input").GetString())));
    }

    [Fact]
    public async Task ReasonBeginningWithByteOrderMarkIsRecordedAndReadByMemberAndOperator()
    {
        await using var fixture = await StartAsync(database, RequestedInstant);
        var package = await fixture.PackageAsync(1, ["Reason Text Shop"]);
        var (purchase, _) = await PurchaseAsync(package, "Month");
        var confirmation = await ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);
        var key = Guid.NewGuid();
        var marked = RequestBody(purchase.OrderId, confirmation, 4500L, ByteOrderMarkReason, key);

        foreach (var body in new[]
        {
            RequestBody(purchase.OrderId, confirmation, 1000L, "Neighbour before"),
            marked,
            RequestBody(purchase.OrderId, confirmation, 2000L, "Neighbour after"),
        })
        {
            using var created = await PostRefundAsync(package.Payer.Client, package.Account, body);
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        }

        // An exact resend from a saved draft is the same request with the same recorded text.
        using var replay = await PostRefundAsync(package.Payer.Client, package.Account, marked);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var replayed = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.Equal(ByteOrderMarkReason, replayed.RootElement.GetProperty("reason").GetString());
        Assert.Equal(3L, await fixture.CountAsync("refund_requests"));

        string[] expected = ["Neighbour after", "Neighbour before", ByteOrderMarkReason];
        using var member = await package.Payer.Client.GetAsync(RefundsPath(package.Account));
        Assert.Equal(expected, await ReasonsAsync(member));

        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        using var intake = await billing.SendAsync(HttpMethod.Get,
            $"/api/admin/accounts/{package.Account:D}/refund-requests");
        Assert.Equal(expected, await ReasonsAsync(intake));
    }

    private static async Task<string[]> ReasonsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        using var page = JsonDocument.Parse(body);
        return page.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("reason").GetString()!).Order(StringComparer.Ordinal).ToArray();
    }
}
