/** Read-only merchant licence notice contracts mirroring LicenseNoticesV1. */
export type LicenseNoticeFailure = 'denied' | 'scope' | 'session' | 'unavailable';

export interface ShopLicenseNoticeItem {
  readonly noticeId: string;
  readonly shopId: string;
  readonly purpose: string;
  readonly threshold: string;
  readonly accessState: string;
  readonly guidanceCode: string;
  readonly templateKey: string;
  readonly dueAt: string;
  readonly effectiveDeadlineAt: string;
  readonly timeZone: string;
  readonly locale: string;
  readonly shopName: string;
}

export interface ShopLicenseNotices {
  readonly schemaVersion: 1;
  readonly shopId: string;
  readonly evaluatedAt: string;
  readonly notices: readonly ShopLicenseNoticeItem[];
  readonly nextDueAt: string | null;
  readonly nextCursor: string | null;
}

export interface AccountLicenseNoticeItem {
  readonly noticeId: string;
  readonly accountId: string;
  readonly purpose: string;
  readonly sourceKind: string;
  readonly sourceId: string;
  readonly threshold: string;
  readonly accessState: string;
  readonly guidanceCode: string;
  readonly templateKey: string;
  readonly dueAt: string;
  readonly effectiveDeadlineAt: string;
  readonly timeZone: string;
  readonly locale: string;
  readonly allowedChannels: readonly string[];
  readonly coveredShopCount: number;
  readonly coveredShopNames: readonly string[];
  readonly evidencedTermPricePaise: number | null;
  readonly evidencedTermCurrency: string | null;
  readonly renewalQuoteRequired: boolean;
  readonly paymentDoesNotLiftSuspension: boolean;
  readonly suspendedShopCount?: number;
  readonly coveredShopNamesTruncated?: boolean;
}

export interface AccountLicenseNotices {
  readonly schemaVersion: 1;
  readonly accountId: string;
  readonly evaluatedAt: string;
  readonly notices: readonly AccountLicenseNoticeItem[];
  readonly nextDueAt: string | null;
  readonly nextCursor: string | null;
}
