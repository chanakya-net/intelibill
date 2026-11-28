using System.Text.Json;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Notifications;

internal sealed record ResolvedCommercialRecipient(
    string RecipientKey,
    Guid? MerchantUserId,
    Guid? ContactId,
    int? ExpectedMemberRevision,
    int? ExpectedContactRevision,
    string Audience,
    string Locale,
    string[] AllowedChannels,
    string? EmailDestination,
    string? DestinationHint);

internal static class NotificationRecipientPlanner
{
    internal static List<ResolvedCommercialRecipient> BuildCommercialRecipients(
        AccountPlanContext context,
        Dictionary<Guid, NoticeRecipientActorV1> verifiedActors)
    {
        var recipients = new List<ResolvedCommercialRecipient>();
        var coveredUsers = new HashSet<Guid>();
        var coveredEmails = new HashSet<string>(StringComparer.Ordinal);

        AddVerifiedMembers(context, verifiedActors, recipients, coveredUsers, coveredEmails);
        AddEligibleContacts(context, verifiedActors, recipients, coveredUsers, coveredEmails);
        return recipients;
    }

    internal static void AddCommercialWrites(
        AccountPlanContext context,
        TermPlanSource termSource,
        SubscriptionAccessFacts facts,
        ShopPlanSource[] activeShops,
        IReadOnlyList<PlannedThreshold> thresholds,
        List<ResolvedCommercialRecipient> recipients,
        List<PlannedNotificationWrite> writes)
    {
        var cancellationVersion = SubscriptionStatusProjection.ActiveCancellation(termSource.Term)?.Version ?? 0L;
        var sourceKind = facts.Basis!.Value.ToString();
        var revision = NotificationSchedulePolicy.BuildCommercialSourceRevision(
            sourceKind,
            termSource.Term.TermId,
            facts,
            termSource.TrialExtensionRevision,
            cancellationVersion,
            termSource.Term.PricePaise,
            termSource.Term.Currency);
        var shopNames = activeShops
            .Select(shop => shop.ShopName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        foreach (var threshold in thresholds)
        {
            var argsJson = JsonSerializer.Serialize(new
            {
                accessState = threshold.AccessStateAtDue,
                guidanceCode = threshold.GuidanceCode,
                coveredShopCount = activeShops.Length,
                coveredShopNames = shopNames,
                evidencedTermPricePaise = termSource.Term.PricePaise,
                evidencedTermCurrency = termSource.Term.Currency,
                renewalQuoteRequired = termSource.Term.Basis == nameof(SubscriptionBasis.Paid),
                paymentDoesNotLiftSuspension = threshold.PaymentDoesNotLiftSuspension,
            });

            foreach (var recipient in recipients)
            {
                writes.Add(new PlannedNotificationWrite(
                    context.AccountId,
                    LicenseNoticesV1.ScopeAccount,
                    context.AccountId,
                    recipient.RecipientKey,
                    recipient.MerchantUserId,
                    recipient.ContactId,
                    recipient.ExpectedMemberRevision,
                    recipient.ExpectedContactRevision,
                    recipient.Audience,
                    threshold.Purpose,
                    threshold.SourceKind,
                    termSource.Term.TermId,
                    revision,
                    threshold.Threshold,
                    threshold.DueAt,
                    threshold.EffectiveDeadlineAt,
                    recipient.Locale,
                    recipient.AllowedChannels,
                    threshold.TemplateKey,
                    argsJson,
                    recipient.EmailDestination,
                    recipient.DestinationHint));
            }
        }
    }

    internal static void AddOperationalShopWrites(
        Guid accountId,
        TermPlanSource termSource,
        SubscriptionAccessFacts facts,
        ShopPlanSource shop,
        IReadOnlyList<PlannedThreshold> thresholds,
        IReadOnlyList<NoticeRecipientActorV1> members,
        HashSet<Guid> commercialUserIds,
        List<PlannedNotificationWrite> writes)
    {
        var cancellationVersion = SubscriptionStatusProjection.ActiveCancellation(termSource.Term)?.Version ?? 0L;
        var sourceKind = facts.Basis!.Value.ToString();
        var revision = NotificationSchedulePolicy.BuildOperationalSourceRevision(
            sourceKind,
            termSource.Term.TermId,
            facts,
            termSource.TrialExtensionRevision,
            cancellationVersion);

        foreach (var threshold in thresholds)
        {
            var argsJson = JsonSerializer.Serialize(new
            {
                shopName = shop.ShopName,
                accessState = threshold.AccessStateAtDue,
                guidanceCode = threshold.GuidanceCode,
                paymentDoesNotLiftSuspension = threshold.PaymentDoesNotLiftSuspension,
            });

            foreach (var member in members)
            {
                if (!member.IsLoginEnabled || (!member.IsEmailVerified && !member.IsPhoneVerified))
                    continue;

                var isShopOwner = string.Equals(member.ShopRole, "Owner", StringComparison.Ordinal);
                var audience = isShopOwner
                    ? LicenseNoticesV1.AudienceShopOwnerOperational
                    : LicenseNoticesV1.AudienceShopStaffOperational;

                var allowOperationalEmail = isShopOwner
                    && !commercialUserIds.Contains(member.UserId)
                    && member.IsEmailVerified
                    && !string.IsNullOrWhiteSpace(member.VerifiedEmail);
                var channels = allowOperationalEmail
                    ? new[] { LicenseNoticesV1.ChannelInApp, LicenseNoticesV1.ChannelEmail }
                    : new[] { LicenseNoticesV1.ChannelInApp };
                var email = allowOperationalEmail ? NotificationPlanner.NormalizeEmail(member.VerifiedEmail!) : null;
                var hint = email is not null ? NotificationPlanner.MaskEmail(email) : null;

                writes.Add(new PlannedNotificationWrite(
                    accountId,
                    LicenseNoticesV1.ScopeShop,
                    shop.ShopId,
                    NotificationPlanner.UserRecipientKey(member.UserId),
                    member.UserId,
                    ContactId: null,
                    ExpectedMemberRevision: null,
                    ExpectedContactRevision: null,
                    audience,
                    threshold.Purpose,
                    threshold.SourceKind,
                    termSource.Term.TermId,
                    revision,
                    threshold.Threshold,
                    threshold.DueAt,
                    threshold.EffectiveDeadlineAt,
                    LicenseNoticesV1.NormalizeLocale(member.Language),
                    channels,
                    threshold.TemplateKey,
                    argsJson,
                    email,
                    hint));
            }
        }
    }

    private static void AddVerifiedMembers(
        AccountPlanContext context,
        Dictionary<Guid, NoticeRecipientActorV1> verifiedActors,
        List<ResolvedCommercialRecipient> recipients,
        HashSet<Guid> coveredUsers,
        HashSet<string> coveredEmails)
    {
        foreach (var member in context.ActiveMembers.OrderBy(item => item.MerchantUserId))
        {
            if (!verifiedActors.TryGetValue(member.MerchantUserId, out var actor))
                continue;
            if (!actor.IsLoginEnabled || (!actor.IsEmailVerified && !actor.IsPhoneVerified))
                continue;

            var verifiedEmail = actor.IsEmailVerified && !string.IsNullOrWhiteSpace(actor.VerifiedEmail)
                ? NotificationPlanner.NormalizeEmail(actor.VerifiedEmail)
                : null;
            var channels = verifiedEmail is not null
                ? new[] { LicenseNoticesV1.ChannelInApp, LicenseNoticesV1.ChannelEmail }
                : new[] { LicenseNoticesV1.ChannelInApp };
            var hint = verifiedEmail is not null ? NotificationPlanner.MaskEmail(verifiedEmail) : null;

            coveredUsers.Add(member.MerchantUserId);
            if (verifiedEmail is not null)
                coveredEmails.Add(verifiedEmail);

            recipients.Add(new ResolvedCommercialRecipient(
                NotificationPlanner.UserRecipientKey(member.MerchantUserId),
                member.MerchantUserId,
                ContactId: null,
                ExpectedMemberRevision: member.Revision,
                ExpectedContactRevision: null,
                LicenseNoticesV1.AudienceCommercialBillingRole,
                LicenseNoticesV1.NormalizeLocale(actor.Language),
                channels,
                verifiedEmail,
                hint));
        }
    }

    private static void AddEligibleContacts(
        AccountPlanContext context,
        Dictionary<Guid, NoticeRecipientActorV1> verifiedActors,
        List<ResolvedCommercialRecipient> recipients,
        HashSet<Guid> coveredUsers,
        HashSet<string> coveredEmails)
    {
        foreach (var contact in context.EligibleContacts.OrderBy(item => item.ContactId))
        {
            var normalizedEmail = NotificationPlanner.NormalizeEmail(contact.NormalizedEmail);
            if (contact.MerchantUserId is { } userId)
            {
                if (coveredUsers.Contains(userId))
                    continue;
                if (!verifiedActors.TryGetValue(userId, out var actor)
                    || !actor.IsLoginEnabled
                    || !actor.IsEmailVerified
                    || string.IsNullOrWhiteSpace(actor.VerifiedEmail)
                    || !string.Equals(NotificationPlanner.NormalizeEmail(actor.VerifiedEmail), normalizedEmail, StringComparison.Ordinal))
                {
                    continue;
                }

                coveredUsers.Add(userId);
                coveredEmails.Add(normalizedEmail);
                recipients.Add(new ResolvedCommercialRecipient(
                    NotificationPlanner.UserRecipientKey(userId),
                    userId,
                    contact.ContactId,
                    ExpectedMemberRevision: null,
                    ExpectedContactRevision: contact.Revision,
                    LicenseNoticesV1.AudienceCommercialContact,
                    LicenseNoticesV1.NormalizeLocale(
                        string.IsNullOrWhiteSpace(contact.Locale) ? actor.Language : contact.Locale),
                    [LicenseNoticesV1.ChannelEmail],
                    normalizedEmail,
                    NotificationPlanner.MaskEmail(normalizedEmail)));
                continue;
            }

            if (coveredEmails.Contains(normalizedEmail))
                continue;

            coveredEmails.Add(normalizedEmail);
            recipients.Add(new ResolvedCommercialRecipient(
                NotificationPlanner.EmailRecipientKey(normalizedEmail),
                MerchantUserId: null,
                contact.ContactId,
                ExpectedMemberRevision: null,
                ExpectedContactRevision: contact.Revision,
                LicenseNoticesV1.AudienceCommercialContact,
                LicenseNoticesV1.NormalizeLocale(contact.Locale),
                [LicenseNoticesV1.ChannelEmail],
                normalizedEmail,
                NotificationPlanner.MaskEmail(normalizedEmail)));
        }
    }
}
