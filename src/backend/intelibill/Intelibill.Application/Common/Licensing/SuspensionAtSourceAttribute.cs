namespace Intelibill.Application.Common.Licensing;

/// <summary>The source resolves the shop or action and checks suspension in its write transaction.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SuspensionAtSourceAttribute : Attribute
{
    public bool CheckRecovery { get; init; }
}
