namespace Intelibill.Application.Common.Interfaces;

public interface IShopUpdatesNotifier
{
    Task NotifyShopUpdateAsync(
        string eventType,
        Guid shopId,
        IReadOnlyCollection<Guid> changedIds,
        DateTimeOffset occurredOn,
        CancellationToken cancellationToken = default);
}
