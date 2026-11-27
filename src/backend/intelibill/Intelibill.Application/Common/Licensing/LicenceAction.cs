namespace Intelibill.Application.Common.Licensing;

/// <summary>Classifies the requested work. A source-linked action still needs proof from its owning transaction.</summary>
public enum LicenceAction
{
    Health,
    Authentication,
    Billing,
    BasicShopSetup,
    NewTrading,
    OperationalCapacity,
    History,
    TillCompletion,
    DueSettlement,
    ExistingPayment,
    SourceCorrection,
    GeneralStock,
    SafetyDisposition,
    Recovery,
    MemberRemoval,
    InternalService,
}
