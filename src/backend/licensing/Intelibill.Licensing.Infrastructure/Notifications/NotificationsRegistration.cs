using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Infrastructure.Status;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal static class NotificationsRegistration
{
    internal static void AddLicenseNotifications(this IServiceCollection services)
    {
        services.AddOptions<NotificationPlanningOptions>()
            .BindConfiguration(NotificationPlanningOptions.SectionName);
        services.AddScoped<NotificationDestinationProtector>();
        services.AddScoped<INotificationPlanStore, NotificationPlanStore>();
        services.AddScoped<ILicenseNoticeReader, NotificationJobReader>();
        services.AddScoped<IBillingContactStore, BillingContactStore>();
        services.AddScoped<IMerchantNoticeRecipientAuthority, MerchantNoticeRecipientAuthorityClient>();
        services.AddScoped(provider => new NotificationPlanner(
            provider.GetRequiredService<INotificationPlanStore>(),
            provider.GetRequiredService<IMerchantNoticeRecipientAuthority>(),
            provider.GetRequiredKeyedService<TimeProvider>(SubscriptionStatusRegistration.ClockKey),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped(provider => new ReadLicenseNoticesHandler(
            provider.GetRequiredService<IBillingDelegationAccess>(),
            provider.GetRequiredService<ISubscriptionStatusStore>(),
            provider.GetRequiredService<ILicenseNoticeReader>(),
            provider.GetRequiredKeyedService<TimeProvider>(SubscriptionStatusRegistration.ClockKey),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped(provider => new BillingContactConsentHandler(
            provider.GetRequiredService<IBillingDelegationAccess>(),
            provider.GetRequiredService<IBillingContactStore>(),
            provider.GetRequiredKeyedService<TimeProvider>(SubscriptionStatusRegistration.ClockKey)));
        services.AddHostedService<NotificationPlanningWorker>();
    }
}
