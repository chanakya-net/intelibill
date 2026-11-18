using System.Net;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>The pinned method allowlist reaches the hosted checkout, and the server still refuses any other method.</summary>
public sealed partial class P0304Tests
{
    [Theory]
    [InlineData("upi", "upi", "card")]
    [InlineData("netbanking,card", "card,netbanking", "upi")]
    public async Task LauncherDeclaresExactlyThePinnedMethodsAndAnExcludedMethodIsNeverFulfilled(string configured,
        string declared, string excluded)
    {
        await using var rig = await Rig.StartAsync(database, methods: configured.Split(','));
        var started = await StartAsync(rig.Payer.Client, rig.Quote, HttpStatusCode.Created);
        var parameters = started.GetProperty("launch").GetProperty("parameters");
        Assert.Equal(declared, parameters.GetProperty("methods").GetString());
        Assert.Equal(declared, (await StatusAsync(rig.Payer.Client, rig.Quote)).GetProperty("launch").GetProperty("parameters")
            .GetProperty("methods").GetString());

        rig.Fake.AddPayment(rig.Fake.OrderId(), "captured", method: excluded);
        var status = await rig.VerifyAsync(started.GetProperty("attemptId").GetGuid(), null, null);
        Assert.NotEqual("Fulfilled", status.GetProperty("fulfilment").GetString());
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("payment_fulfilments"));
    }
}
