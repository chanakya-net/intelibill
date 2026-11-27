using System.Net;
using System.Text.Json;

namespace Intelibill.Integration.Tests;

public sealed partial class P0413dLicensingTests
{
    [Fact]
    public async Task Committed_correction_replays_and_looks_up_only_under_current_authority()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        using var manager = CreateClient(host);
        var source = await SeedAsync(owner);
        await AddMemberAsync(host, owner, manager, source.Shop, "Manager");
        await ExpireAsync(owner, source);
        var version = await ReceiptVersionAsync(source.Receipt);
        var key = NewKey();
        var corrections = await Task.WhenAll(
            CorrectAsync(owner, source.Receipt, version, 1100, key),
            CorrectAsync(owner, source.Receipt, version, 1100, key));
        using var firstResponse = corrections[0];
        using var duplicateResponse = corrections[1];
        var committed = await ReadSuccessAsync(firstResponse);
        Assert.True(JsonElement.DeepEquals(committed, await ReadSuccessAsync(duplicateResponse)));

        using var second = await CorrectAsync(owner, source.Receipt, await ReceiptVersionAsync(source.Receipt), 1050, NewKey());
        await ReadSuccessAsync(second);
        var before = await FactsAsync(source);

        using var laterReplay = await CorrectAsync(owner, source.Receipt, version, 1100, key);
        Assert.True(JsonElement.DeepEquals(committed, await ReadSuccessAsync(laterReplay)));
        var lookup = $"/api/financial-operations/receipt-cost-correction/{key}";
        using var managerLookup = await manager.GetAsync(lookup, Ct);
        var recovered = await ReadSuccessAsync(managerLookup);
        Assert.Equal("receipt-cost-correction", recovered.GetProperty("operationType").GetString());
        Assert.Equal(key, recovered.GetProperty("operationKey").GetString());
        Assert.Equal(source.Receipt, recovered.GetProperty("targetId").GetGuid());
        Assert.True(JsonElement.DeepEquals(committed, recovered.GetProperty("response")));

        await AssertProblemAsync(await CorrectAsync(owner, Guid.NewGuid(), version, 1100, key),
            HttpStatusCode.Conflict, "Operation.PayloadMismatch");
        using var managerPost = await CorrectAsync(manager, source.Receipt, version, 1100, key);
        Assert.Equal(HttpStatusCode.Forbidden, managerPost.StatusCode);
        var miss = $"/api/financial-operations/receipt-cost-correction/{NewKey()}";
        await AssertProblemAsync(await owner.GetAsync(miss, Ct), HttpStatusCode.NotFound, "Operation.NotFound");

        await ApplyAsync(source.Binding, _clock.Now.AddDays(1), 2, securityEffectiveAt: _clock.Now);
        await AssertProblemAsync(await owner.GetAsync(lookup, Ct), HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        await AssertProblemAsync(await owner.GetAsync(miss, Ct), HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        await AssertProblemAsync(await CorrectAsync(owner, source.Receipt, version, 1100, key),
            HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        Assert.Equal(before, await FactsAsync(source));
        _output.WriteLine("Concurrent exact-key replay, stale-outcome lookup by manager, changed target 409, miss 404, and security 403 for hits and misses. Unchanged {0}.", before);
    }
}
