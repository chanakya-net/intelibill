namespace Intelibill.Licensing.Application.Notifications;

public interface IBillingEmailDispatcher
{
    Task<bool> DeliverOneAsync(CancellationToken cancellationToken);
}
