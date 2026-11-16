using System.Globalization;
using System.Text.Json;
using Intelibill.Licensing.Application.Grants;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0212Tests
{
    private static readonly JsonElement GrantContract = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectPaths.Root,
        "tests/contracts/licensing/complimentary-grant-v1.json"))).RootElement;

    [Fact]
    public void ContractSelectionVectorsChooseTheDocumentedSourceAtEachInstant()
    {
        var grant = GrantRow();
        foreach (var vector in GrantContract.GetProperty("selection").EnumerateArray())
        {
            var name = vector.GetProperty("name").GetString();
            var baseline = vector.GetProperty("baseline").GetString() is { } key ? BaselineRow(key) : null;
            var suspensions = vector.TryGetProperty("suspensions", out var list)
                ? list.EnumerateArray().Select(item => new SuspensionInterval(Instant(item.GetProperty("startsAt")),
                    item.TryGetProperty("releasedAt", out var released) ? Instant(released) : null)).ToArray()
                : null;
            var chosen = SubscriptionStatusProjection.Choose(new ShopCoverage(grant.AccountId, baseline, [grant]),
                Instant(vector.GetProperty("at")), commercial: true, suspensions);
            Assert.NotNull(chosen);
            var expectGrant = vector.GetProperty("source").GetString() == "grant";
            Assert.True((expectGrant ? grant.TermId : baseline!.TermId) == chosen.TermId, $"{name}: source");
            Assert.True(vector.GetProperty("state").GetString() == chosen.State, $"{name}: {chosen.State}");
            if (expectGrant)
            {
                Assert.Equal("Complimentary", chosen.Basis);
                Assert.Null(chosen.GraceEndsAt);
                Assert.Null(chosen.NextPeriodEndsAt);
                Assert.Null(chosen.Commercial);
                Assert.Equal(grant.EndsAt, chosen.AccessEndsAt);
            }
            if (vector.TryGetProperty("accessEndsAt", out var accessEnds))
                Assert.Equal(Instant(accessEnds), chosen.AccessEndsAt);
        }
    }

    [Fact]
    public void ContractInstantsCapacityAndStatusExampleMatchTheImplementation()
    {
        var instants = GrantContract.GetProperty("instants");
        foreach (var accepted in instants.GetProperty("accepted").EnumerateArray())
            Assert.NotNull(ComplimentaryGrantFacts.ParseInstant(accepted.GetString()));
        foreach (var refused in instants.GetProperty("refused").EnumerateArray())
        {
            var parsed = ComplimentaryGrantFacts.ParseInstant(refused.GetString());
            Assert.True(parsed is null || parsed.Value.UtcTicks % 10 != 0, refused.GetRawText());
        }
        var canonical = instants.GetProperty("canonical");
        Assert.Equal(canonical.GetProperty("output").GetString(),
            ComplimentaryGrantFacts.Format(ComplimentaryGrantFacts.ParseInstant(canonical.GetProperty("input").GetString())!.Value));
        foreach (var row in GrantContract.GetProperty("capacity").EnumerateArray())
            Assert.Equal(row.GetProperty("coverageMode").GetString(), ComplimentaryAccessPolicy.CoverageMode(row.GetProperty("shops").GetInt32()));
        Assert.Equal(ComplimentaryAccessPolicy.Version, GrantContract.GetProperty("policyVersion").GetString());
        Assert.Equal(ComplimentaryAccessPolicy.MaximumShops, GrantContract.GetProperty("limits").GetProperty("maximumShops").GetInt32());

        var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/subscription-status-v1.json"))).RootElement;
        var example = status.GetProperty("complimentaryExample").Deserialize<SubscriptionTermStatusV1>()!;
        var projected = SubscriptionStatusProjection.Project(GrantRow() with { TermId = example.TermId },
            Instant(status.GetProperty("example").GetProperty("evaluatedAt")).AddDays(28), commercial: true);
        Assert.Equal(example, projected);
        Assert.Contains("Complimentary", SubscriptionStatusV1.Bases);
    }

    private static TermFactsRow GrantRow()
    {
        var term = GrantContract.GetProperty("terms").GetProperty("grant");
        return new TermFactsRow(Guid.Parse("00000000-0000-0000-0000-000000000099"), Guid.NewGuid(), "Grouped", 2,
            Instant(term.GetProperty("startsAt")), Instant(term.GetProperty("endsAt")), null, null, null, null, null, null,
            null, null, null, null, null, null, null,
            Complimentary: new ComplimentaryTermProvenance(ComplimentaryAccessPolicy.Version, 4, 2, Consistent: true));
    }

    private static TermFactsRow BaselineRow(string key)
    {
        var term = GrantContract.GetProperty("terms").GetProperty(key);
        var (starts, ends) = (Instant(term.GetProperty("startsAt")), Instant(term.GetProperty("endsAt")));
        return term.GetProperty("basis").GetString() switch
        {
            "Paid" => new TermFactsRow(Guid.NewGuid(), Guid.NewGuid(), "Independent", 1, starts, ends, "Paid", "Month",
                DateTime.Parse(term.GetProperty("anchorLocal").GetString()!, CultureInfo.InvariantCulture), BillingCalendar.Zone,
                term.GetProperty("ordinal").GetInt32(), 5, 149900, "INR", null, "local-fixture-p0212", true,
                BillingCalendar.Version, GracePolicy.Version),
            "Trial" => new TermFactsRow(Guid.NewGuid(), Guid.NewGuid(), "Independent", 1, starts, ends, null, null, null, null,
                null, null, null, null, null, null, null, null, null,
                new TrialTermProvenance(TrialHistory.ComputationVersion, 5, 1, 1, DevelopmentFixture: true)),
            _ => new TermFactsRow(Guid.NewGuid(), Guid.NewGuid(), "Independent", 1, starts, ends, null, null, null, null,
                null, null, null, null, null, null, null, null, null),
        };
    }

    private static DateTimeOffset Instant(JsonElement value) =>
        DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
