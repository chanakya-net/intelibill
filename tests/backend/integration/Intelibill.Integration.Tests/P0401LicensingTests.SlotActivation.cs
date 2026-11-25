using Intelibill.Application.Common.Licensing;

namespace Intelibill.Integration.Tests;

public sealed partial class P0401LicensingTests
{
    [Theory]
    [MemberData(nameof(Matrix))]
    public void Pending_slot_activation_keeps_an_operating_head_in_the_pending_row(LicenceAction action,
        LicenceOutcome pending, LicenceOutcome operating, LicenceOutcome restricted, LicenceOutcome security)
    {
        var policy = new LicenceActionPolicy();
        var now = Deadline.AddTicks(-1);
        var unrestricted = Account("Unrestricted");

        foreach (var head in new[] { ShopState("Active", "Paid"), ShopState("Grace", "Paid") })
        {
            Assert.Equal(operating, PendingSlot(policy, action, head, unrestricted, now, activationPending: false).Outcome);
            var decision = PendingSlot(policy, action, head, unrestricted, now, activationPending: true);
            Assert.Equal(pending, decision.Outcome);
            Assert.Equal("Licence.PendingActivation", decision.OperatingRefusal?.Code);
            Assert.Null(decision.ValidUntil);
        }

        // The flag never loosens or relabels a head that could not operate anyway.
        var active = ShopState("Active", "Paid");
        Assert.Equal(restricted, PendingSlot(policy, action, active, Account("Trading"), now, activationPending: true).Outcome);
        Assert.Equal(security, PendingSlot(policy, action, active, Account("Security"), now, activationPending: true).Outcome);
        Assert.Equal(restricted, PendingSlot(policy, action, ShopState("Expired"), unrestricted, now, activationPending: true).Outcome);
        Assert.Equal(restricted, PendingSlot(policy, action, active, unrestricted, Deadline, activationPending: true).Outcome);
    }

    private static LicenceDecision PendingSlot(LicenceActionPolicy policy, LicenceAction action,
        Domain.Entities.ShopLicenseState shop, Domain.Entities.AccountLicenseRestriction account, DateTimeOffset now,
        bool activationPending) =>
        policy.Decide(action, shop, account, new(shop.UpstreamVerifiedAt, account.UpstreamVerifiedAt), now, activationPending);
}
