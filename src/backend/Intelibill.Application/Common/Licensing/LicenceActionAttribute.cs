namespace Intelibill.Application.Common.Licensing;

/// <summary>Inventory metadata for a merchant entry point.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
public sealed class LicenceActionAttribute(LicenceAction action) : Attribute
{
    public LicenceAction Action { get; } = action;
}
