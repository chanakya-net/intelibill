using System.Text.Json;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0210Tests
{
    [Fact]
    public void LocalTrialContractKeepsFixedDurationAndItsExactEnd()
    {
        using var corpus = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/shop-trial-v1.json")));
        var input = corpus.RootElement;
        var acceptance = input.GetProperty("acceptance").Deserialize<ShopTrialAcceptanceV1>()!;
        Assert.True(ShopTrialsV1.Valid(acceptance));
        Assert.False(ShopTrialsV1.Valid(acceptance with { TermsAccepted = false }));
        Assert.False(ShopTrialsV1.Valid(acceptance with { OperationKey = Guid.Empty }));

        var start = input.GetProperty("startsAt").GetDateTimeOffset();
        var end = input.GetProperty("endsAt").GetDateTimeOffset();
        Assert.Equal(start.AddDays(ShopTrialsV1.DurationDays), end);
        Assert.Equal(ShopTrialsV1.DurationDays, input.GetProperty("durationDays").GetInt32());
        var row = new TermFactsRow(Guid.NewGuid(), Guid.NewGuid(), "Independent", 1, start, end, null, null, null,
            null, null, null, null, null, null, null, null, null, null,
            new TrialTermProvenance(TrialHistory.ComputationVersion, 5, 1, 1, DevelopmentFixture: true));

        var active = SubscriptionStatusProjection.Project(row, end.AddTicks(-10), commercial: true);
        var expired = SubscriptionStatusProjection.Project(row, end, commercial: true);
        Assert.Equal("Active", active.State);
        Assert.Equal(input.GetProperty("statusAtEnd").GetString(), expired.State);
        Assert.Equal(input.GetProperty("timeZone").GetString(), active.TimeZone);
        Assert.Equal(end, active.AccessEndsAt);
        Assert.Null(expired.GraceEndsAt);
        Assert.Null(expired.Commercial);
        Assert.Null(expired.NextPeriodEndsAt);
    }
}
