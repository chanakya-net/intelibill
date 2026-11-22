using Intelibill.Application.Common.Licensing;
using LicenceErrors = Intelibill.Application.Common.Errors.Errors.Licence;

namespace Intelibill.Application.Unit.Tests.Common.Licensing;

public sealed class LicenceGateTests
{
    [Fact]
    public void Existing_source_must_be_strictly_before_the_known_cutoff()
    {
        var cutoff = new DateTimeOffset(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
        var decision = new LicenceDecision(LicenceOutcome.RequiresProvenSource, Enforced: true)
        {
            SourceCompletionCutoff = cutoff,
            OperatingRefusal = LicenceErrors.TradingRestricted,
        };

        Assert.Equal(LicenceErrors.TradingRestricted, decision.ExistingSourceDenial(cutoff));
        Assert.Null(decision.ExistingSourceDenial(cutoff.AddTicks(-1)));
        Assert.Null((decision with { SourceCompletionCutoff = null }).ExistingSourceDenial(cutoff));
    }
}
