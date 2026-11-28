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
    byte[]? EmailDestinationHash,
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
        NoticeRestrictionScope restriction,
        int suspendedShopCount,
        string rosterFingerprint,
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
        var shopSample = LicenseNoticesV1.BoundShopNameSample(activeShops.Select(shop => shop.ShopName));

        foreach (var threshold in thresholds)
        {
            var argsJson = SerializeCommercialArgs(
                termSource,
                activeShops.Length,
                suspendedShopCount,
                shopSample,
                threshold);
            foreach (var recipient in recipients)
            {
                writes.Add(BuildCommercialWrite(
                    context.AccountId,
                    termSource.Term.TermId,
                    revision,
                    restriction,
                    rosterFingerprint,
                    threshold,
                    argsJson,
                    recipient));
            }
        }
    }

    internal static void AddOperationalShopWrites(
        Guid accountId,
        TermPlanSource termSource,
        SubscriptionAccessFacts facts,
        ShopPlanSource shop,
        NoticeRestrictionScope restriction,
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
        var boundedShopName = LicenseNoticesV1.BoundShopName(shop.ShopName);

        foreach (var threshold in thresholds)
        {
            var argsJson = SerializeOperationalArgs(boundedShopName, threshold);
            foreach (var member in members)
            {
                if (TryBuildOperationalWrite(
                    accountId,
                    termSource.Term.TermId,
                    revision,
                    shop,
                    restriction,
                    threshold,
                    argsJson,
                    member,
                    commercialUserIds) is { } write)
                {
                    writes.Add(write);
                }
            }
        }
    }

    private static string SerializeCommercialArgs(
        TermPlanSource termSource,
        int coveredShopCount,
        int suspendedShopCount,
        string[] shopSample,
        PlannedThreshold threshold) =>
        JsonSerializer.Serialize(new
        {
            accessState = threshold.AccessStateAtDue,
            guidanceCode = threshold.GuidanceCode,
            coveredShopCount,
            suspendedShopCount,
            coveredShopNames = shopSample,
            coveredShopNamesTruncated = coveredShopCount > shopSample.Length,
            evidencedTermPricePaise = termSource.Term.PricePaise,
            evidencedTermCurrency = termSource.Term.Currency,
            renewalQuoteRequired = termSource.Term.Basis == nameof(SubscriptionBasis.Paid),
            paymentDoesNotLiftSuspension = threshold.PaymentDoesNotLiftSuspension,
        });

    private static PlannedNotificationWrite BuildCommercialWrite(
        Guid accountId,
        Guid termId,
        string revision,
        NoticeRestrictionScope restriction,
        string rosterFingerprint,
        PlannedThreshold threshold,
        string argsJson,
        ResolvedCommercialRecipient recipient) =>
        new(
            accountId,
            LicenseNoticesV1.ScopeAccount,
            accountId,
            recipient.RecipientKey,
            recipient.MerchantUserId,
            recipient.ContactId,
            recipient.ExpectedMemberRevision,
            recipient.ExpectedContactRevision,
            ExpectedAffiliationRequestId: null,
            ExpectedRestriction: restriction,
            ExpectedRosterFingerprint: rosterFingerprint,
            recipient.Audience,
            threshold.Purpose,
            threshold.SourceKind,
            termId,
            revision,
            threshold.Threshold,
            threshold.DueAt,
            threshold.EffectiveDeadlineAt,
            recipient.Locale,
            recipient.AllowedChannels,
            threshold.TemplateKey,
            argsJson,
            recipient.EmailDestination,
            recipient.EmailDestinationHash,
            recipient.DestinationHint);

    private static string SerializeOperationalArgs(string boundedShopName, PlannedThreshold threshold) =>
        JsonSerializer.Serialize(new
        {
            shopName = boundedShopName,
            accessState = threshold.AccessStateAtDue,
            guidanceCode = threshold.GuidanceCode,
            paymentDoesNotLiftSuspension = threshold.PaymentDoesNotLiftSuspension,
        });

    private static PlannedNotificationWrite? TryBuildOperationalWrite(
        Guid accountId,
        Guid termId,
        string revision,
        ShopPlanSource shop,
        NoticeRestrictionScope restriction,
        PlannedThreshold threshold,
        string argsJson,
        NoticeRecipientActorV1 member,
        HashSet<Guid> commercialUserIds)
    {
        if (!member.IsLoginEnabled || (!member.IsEmailVerified && !member.IsPhoneVerified))
            return null;

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
        var emailHash = email is not null ? NotificationPlanner.HashEmail(email) : null;
        var hint = email is not null ? NotificationPlanner.MaskEmail(email) : null;

        return new PlannedNotificationWrite(
            accountId,
            LicenseNoticesV1.ScopeShop,
            shop.ShopId,
            NotificationPlanner.UserRecipientKey(member.UserId),
            member.UserId,
            ContactId: null,
            ExpectedMemberRevision: null,
            ExpectedContactRevision: null,
            ExpectedAffiliationRequestId: shop.AffiliationRequestId,
            ExpectedRestriction: restriction,
            ExpectedRosterFingerprint: null,
            audience,
            threshold.Purpose,
            threshold.SourceKind,
            termId,
            revision,
            threshold.Threshold,
            threshold.DueAt,
            threshold.EffectiveDeadlineAt,
            LicenseNoticesV1.NormalizeLocale(member.Language),
            channels,
            threshold.TemplateKey,
            argsJson,
            email,
            emailHash,
            hint);
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
            var emailHash = verifiedEmail is not null ? NotificationPlanner.HashEmail(verifiedEmail) : null;
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
                emailHash,
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
            var resolved = contact.MerchantUserId is { } userId
                ? TryResolveIdentityContact(contact, userId, verifiedActors, coveredUsers, coveredEmails)
                : TryResolveStandaloneContact(contact, coveredEmails);
            if (resolved is not null)
                recipients.Add(resolved);
        }
    }

    private static ResolvedCommercialRecipient? TryResolveIdentityContact(
        BillingContactSnapshot contact,
        Guid userId,
        Dictionary<Guid, NoticeRecipientActorV1> verifiedActors,
        HashSet<Guid> coveredUsers,
        HashSet<string> coveredEmails)
    {
        var normalizedEmail = NotificationPlanner.NormalizeEmail(contact.NormalizedEmail);
        if (coveredUsers.Contains(userId) || coveredEmails.Contains(normalizedEmail))
            return null;
        if (!verifiedActors.TryGetValue(userId, out var actor)
            || !actor.IsLoginEnabled
            || !actor.IsEmailVerified
            || string.IsNullOrWhiteSpace(actor.VerifiedEmail)
            || !string.Equals(
                NotificationPlanner.NormalizeEmail(actor.VerifiedEmail),
                normalizedEmail,
                StringComparison.Ordinal))
        {
            return null;
        }

        coveredUsers.Add(userId);
        coveredEmails.Add(normalizedEmail);
        return new ResolvedCommercialRecipient(
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
            contact.DestinationHash,
            NotificationPlanner.MaskEmail(normalizedEmail));
    }

    private static ResolvedCommercialRecipient? TryResolveStandaloneContact(
        BillingContactSnapshot contact,
        HashSet<string> coveredEmails)
    {
        var normalizedEmail = NotificationPlanner.NormalizeEmail(contact.NormalizedEmail);
        if (!coveredEmails.Add(normalizedEmail))
            return null;

        return new ResolvedCommercialRecipient(
            NotificationPlanner.EmailRecipientKey(normalizedEmail),
            MerchantUserId: null,
            contact.ContactId,
            ExpectedMemberRevision: null,
            ExpectedContactRevision: contact.Revision,
            LicenseNoticesV1.AudienceCommercialContact,
            LicenseNoticesV1.NormalizeLocale(contact.Locale),
            [LicenseNoticesV1.ChannelEmail],
            normalizedEmail,
            contact.DestinationHash,
            NotificationPlanner.MaskEmail(normalizedEmail));
    }
}
