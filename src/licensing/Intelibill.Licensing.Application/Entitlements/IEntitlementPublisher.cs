namespace Intelibill.Licensing.Application.Entitlements;

/// <summary>Appends a snapshot to the current account-fenced licensing transaction.</summary>
public interface IEntitlementPublisher
{
    Task PublishAsync(Guid account, Guid shop, CancellationToken ct);
}
