using Intelibill.Application.Common.Licensing;
using LicenceErrors = Intelibill.Application.Common.Errors.Errors.Licence;

namespace Intelibill.Application.Unit.Tests.Common.Licensing;

public class MemberSeatPolicyTests
{
    private static ShopLicenceLease Lease(LicenceDecision decision, int? allowance) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, 1, decision) { MemberAllowance = allowance };

    private static readonly LicenceDecision Operating = new(LicenceOutcome.Allowed, Enforced: true);

    [Theory]
    [InlineData(2, 3)]
    [InlineData(0, 1)]
    public void AdmitsBelowTheAllowance(int active, int allowance) =>
        Assert.Null(MemberSeatPolicy.AdmissionDenial(Lease(Operating, allowance), active, discloseUsage: true));

    [Theory]
    [InlineData(3, 3)]
    [InlineData(5, 3)]
    public void RefusesAtOrAboveTheAllowanceAndDisclosesOnlyWhenAsked(int active, int allowance)
    {
        var owner = MemberSeatPolicy.AdmissionDenial(Lease(Operating, allowance), active, discloseUsage: true);
        var invitee = MemberSeatPolicy.AdmissionDenial(Lease(Operating, allowance), active, discloseUsage: false);

        Assert.Equal(LicenceErrors.MemberCapacityReachedCode, owner?.Code);
        Assert.Equal(allowance, owner?.Metadata?["memberAllowance"]);
        Assert.Equal(active, owner?.Metadata?["activeMembers"]);
        Assert.Equal(LicenceErrors.MemberCapacityReachedCode, invitee?.Code);
        Assert.Null(invitee?.Metadata);
    }

    [Fact]
    public void ObserveOnlyShopsAreNotCapped()
    {
        var lease = Lease(new LicenceDecision(LicenceOutcome.Blocked, Enforced: false, LicenceErrors.Expired), 1);

        Assert.False(MemberSeatPolicy.Applies(lease));
        Assert.Null(MemberSeatPolicy.AdmissionDenial(lease, 9, discloseUsage: true));
    }

    [Fact]
    public void AnUnknownAllowanceProvesNoFreeSeat() =>
        Assert.Equal(LicenceErrors.VerificationUnavailableCode,
            MemberSeatPolicy.AdmissionDenial(Lease(Operating, null), 0, discloseUsage: true)?.Code);

    [Fact]
    public void ARestrictedShopAnswersItsOwnRefusal() =>
        Assert.Equal(LicenceErrors.Expired.Code, MemberSeatPolicy.AdmissionDenial(
            Lease(new LicenceDecision(LicenceOutcome.Blocked, Enforced: true, LicenceErrors.Expired), 5), 0, true)?.Code);

    [Fact]
    public void TheDecisionIsReevaluatedAtTheAdmissionInstant()
    {
        var lease = Lease(Operating, 5) with
        {
            Reevaluate = () => new LicenceDecision(LicenceOutcome.Blocked, Enforced: true, LicenceErrors.Expired),
        };

        Assert.Equal(LicenceErrors.Expired.Code, MemberSeatPolicy.AdmissionDenial(lease, 0, true)?.Code);
    }
}
