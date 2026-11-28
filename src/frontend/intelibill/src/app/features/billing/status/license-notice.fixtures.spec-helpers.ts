import type { AccountLicenseNotices, ShopLicenseNotices } from './license-notice.models';

export function shopNotices(
  shopId: string,
  shopName = 'North Shop',
  options: { nextDueAt?: string | null; guidanceCode?: string; accessState?: string; evaluatedAt?: string } = {},
): ShopLicenseNotices {
  return {
    schemaVersion: 1,
    shopId,
    evaluatedAt: options.evaluatedAt ?? '2028-02-22T04:30:00Z',
    notices: [
      {
        noticeId: `notice-${shopId}`,
        shopId,
        purpose: options.guidanceCode?.startsWith('Grace') ? 'GraceNotice' : 'PaidRenewalReminder',
        threshold: options.guidanceCode === 'GraceEnding' ? 'GraceMinus1Day' : 'PaidMinus7Days',
        accessState: options.accessState ?? 'Active',
        guidanceCode: options.guidanceCode ?? 'PaidRenewalDue',
        templateKey: 'notice.paid.minus7',
        dueAt: '2028-02-22T04:30:00Z',
        effectiveDeadlineAt: '2028-02-29T04:30:00Z',
        timeZone: 'Asia/Kolkata',
        locale: 'en-IN',
        shopName,
      },
    ],
    nextDueAt: options.nextDueAt ?? null,
    nextCursor: null,
  };
}

export function accountNotices(
  accountId: string,
  options: {
    suspended?: boolean;
    guidanceCode?: string;
    coveredShopCount?: number;
    coveredShopNames?: string[];
    evaluatedAt?: string;
    nextDueAt?: string | null;
  } = {},
): AccountLicenseNotices {
  const names = options.coveredShopNames ?? ['North Shop', 'South Shop'];
  const count = options.coveredShopCount ?? names.length;
  return {
    schemaVersion: 1,
    accountId,
    evaluatedAt: options.evaluatedAt ?? '2028-02-22T04:30:00Z',
    notices: [
      {
        noticeId: `acc-notice-${accountId}`,
        accountId,
        purpose: 'PaidRenewalReminder',
        sourceKind: 'Paid',
        sourceId: '44444444-4444-4444-8444-444444444444',
        threshold: 'PaidMinus7Days',
        accessState: options.suspended ? 'Suspended' : 'Active',
        guidanceCode: options.guidanceCode ?? (options.suspended ? 'SuspendedNoPaymentLift' : 'PaidRenewalDue'),
        templateKey: 'notice.paid.minus7',
        dueAt: '2028-02-22T04:30:00Z',
        effectiveDeadlineAt: '2028-02-29T04:30:00Z',
        timeZone: 'Asia/Kolkata',
        locale: 'en-IN',
        allowedChannels: ['InApp', 'Email'],
        coveredShopCount: count,
        coveredShopNames: names,
        coveredShopNamesTruncated: count > names.length,
        evidencedTermPricePaise: 12000,
        evidencedTermCurrency: 'INR',
        renewalQuoteRequired: true,
        paymentDoesNotLiftSuspension: Boolean(options.suspended),
      },
    ],
    nextDueAt: options.nextDueAt ?? null,
    nextCursor: null,
  };
}
