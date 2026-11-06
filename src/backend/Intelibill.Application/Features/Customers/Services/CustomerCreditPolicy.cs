using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Customers.Services;

/// <summary>The customer facts a credit decision is made on, read inside the decision's lock.</summary>
public sealed record CustomerCreditState(bool IsActive, CreditPolicyMode Mode, decimal CreditLimit)
{
    public static CustomerCreditState Of(Customer customer) =>
        new(customer.IsActive, customer.CreditPolicyMode, customer.CreditLimit);
}

/// <summary>An owner's or manager's approval to go over an enforced limit, with what they saw.</summary>
public sealed record CreditOverrideRequest(string Reason, decimal ObservedExposure);

public enum CreditDecisionKind
{
    /// <summary>The sale adds no due, so no credit decision applies.</summary>
    NoNewDue = 1,

    /// <summary>The due fits the enforced limit.</summary>
    WithinLimit = 2,

    /// <summary>A legacy advisory setting: the due is accepted and not enforced.</summary>
    Advisory = 3,

    /// <summary>The due exceeds the enforced limit and an authorized override approved it.</summary>
    Overridden = 4,
}

public sealed record CreditDecision(
    CreditDecisionKind Kind,
    decimal ExposureBefore,
    decimal ExposureAfter,
    decimal CreditLimit,
    CreditOverrideRequest? Override = null)
{
    /// <summary>True when an advisory (unenforced) limit is exceeded: shown, never refused.</summary>
    public bool ExceedsAdvisoryLimit =>
        Kind == CreditDecisionKind.Advisory && CreditLimit > 0m && ExposureAfter > CreditLimit;
}

/// <summary>
/// Decides whether new due may be added to a customer's account. Exposure is the signed account
/// balance (due less payments and returns, plus reversals): unapplied payments and reversals are
/// counted once, and a customer in credit has negative exposure. It must be read under the
/// customer's row lock, so two concurrent sales cannot both take the last of the limit.
/// </summary>
public static class CustomerCreditPolicy
{
    public const int MaxOverrideReasonLength = 500;

    /// <summary>New due the account can take now; null when the limit is advisory (not enforced).</summary>
    public static decimal? AvailableCredit(CustomerCreditState state, decimal currentExposure)
    {
        if (!state.IsActive || state.Mode == CreditPolicyMode.NoCredit)
            return 0m;

        return state.Mode == CreditPolicyMode.Enforced
            ? Math.Max(0m, state.CreditLimit - currentExposure)
            : null;
    }

    public static ErrorOr<CreditDecision> Evaluate(
        CustomerCreditState state,
        decimal currentExposure,
        decimal newDue,
        CreditOverrideRequest? creditOverride,
        ShopRole? actorRole)
    {
        var exposureAfter = currentExposure + newDue;
        if (newDue <= 0m)
            return new CreditDecision(CreditDecisionKind.NoNewDue, currentExposure, currentExposure, state.CreditLimit);

        if (!state.IsActive)
            return Errors.CustomerCredit.CustomerInactive;

        switch (state.Mode)
        {
            case CreditPolicyMode.NoCredit:
                return Errors.CustomerCredit.NotExtended;

            case CreditPolicyMode.LegacyAdvisory:
                return new CreditDecision(CreditDecisionKind.Advisory, currentExposure, exposureAfter, state.CreditLimit);

            case CreditPolicyMode.Enforced:
                if (exposureAfter <= state.CreditLimit)
                    return new CreditDecision(CreditDecisionKind.WithinLimit, currentExposure, exposureAfter, state.CreditLimit);

                if (creditOverride is null)
                    return Errors.CustomerCredit.LimitExceeded;

                if (actorRole is not (ShopRole.Owner or ShopRole.Manager))
                    return Errors.CustomerCredit.OverrideNotPermitted;

                if (string.IsNullOrWhiteSpace(creditOverride.Reason) || creditOverride.Reason.Trim().Length > MaxOverrideReasonLength)
                    return Errors.CustomerCredit.OverrideReasonRequired;

                // The approval covers the exposure the approver saw; more debt since then needs a new look.
                if (currentExposure > creditOverride.ObservedExposure)
                    return Errors.CustomerCredit.OverrideStale;

                return new CreditDecision(CreditDecisionKind.Overridden, currentExposure, exposureAfter, state.CreditLimit, creditOverride);

            default:
                return Errors.CustomerCredit.NotExtended;
        }
    }
}
