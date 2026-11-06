namespace Intelibill.Domain.Enums;

/// <summary>How a customer may add new due (credit) to their account.</summary>
public enum CreditPolicyMode
{
    /// <summary>No new due may be added; every sale is paid in full.</summary>
    NoCredit = 1,

    /// <summary>New due is refused once the account would exceed the saved credit limit.</summary>
    Enforced = 2,

    /// <summary>
    /// A setting recorded before credit policies existed: the limit is shown but not enforced until
    /// the merchant reviews it. Never presented as enforced.
    /// </summary>
    LegacyAdvisory = 3,
}
