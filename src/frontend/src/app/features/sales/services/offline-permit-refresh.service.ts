import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { readRestriction } from '../../../core/context/shop-context.service';
import { AuthService } from '../../../core/auth/auth.service';
import { InvoiceLeaseIndexedDbService, INVOICE_LEASE_RENEWAL_THRESHOLD, type InvoiceLeaseSnapshot } from '../../../core/storage/invoice-lease-indexeddb.service';
import { OfflineSalesDeviceSettingsStorage, type OfflineSalesDeviceSettings, type PermitRefreshNotice } from '../../../core/storage/offline-sales-device-settings.storage';
import { isShopTradingAuthority, type ShopTradingAuthority } from '../../billing/status/subscription-status.models';
import { SubscriptionStatusService, TradingAuthorityReadError } from '../../billing/status/subscription-status.service';
import { TillContextService } from '../../tills/services/till-context.service';
import { isValidLeaseLicenceEvidence, parseInstantNanos, toOfflinePermitDisplaySummary, verifyDeliveredOfflineLicencePermit, type OfflineLicencePermitBinding } from '../utils/offline-licence-permit';
import { isFreshCandidate, isNewReservation, shouldReserveForRefresh } from '../utils/offline-permit-refresh';
import { OFFLINE_PERMIT_PUBLIC_KEYS, verifyOfflinePermitSignature, type VerifiedOfflinePermit } from '../utils/offline-permit-signature';
import { OfflineIssuanceAuthorityService, storedMatchesSigned, type OfflineIssuanceInvocation } from './offline-issuance-authority.service';
import { OfflineLicencePermitService } from './offline-licence-permit.service';
import { OfflineSalesSnapshotSyncService, type PromotePermitCandidate } from './offline-sales-snapshot-sync.service';
import { SaleService } from './sale.service';

interface RefreshAttempt {
  readonly invocation: OfflineIssuanceInvocation;
  readonly previous: InvoiceLeaseSnapshot;
  readonly held: VerifiedOfflinePermit;
}

@Injectable({ providedIn: 'root' })
export class OfflinePermitRefreshService {
  private readonly auth = inject(AuthService);
  private readonly authority = inject(OfflineIssuanceAuthorityService);
  private readonly leases = inject(InvoiceLeaseIndexedDbService);
  private readonly settings = inject(OfflineSalesDeviceSettingsStorage);
  private readonly status = inject(SubscriptionStatusService);
  private readonly sales = inject(SaleService);
  private readonly permits = inject(OfflineLicencePermitService);
  private readonly snapshots = inject(OfflineSalesSnapshotSyncService);
  private readonly pins = inject(OFFLINE_PERMIT_PUBLIC_KEYS);
  private readonly till = inject(TillContextService);

  /** True when this cycle handled the snapshot or must preserve the previous usable one. */
  async refresh(shopId: string): Promise<boolean> {
    const attempt = await this.capture(shopId);
    if (!attempt || !this.current(attempt)) return false;
    let preview: ShopTradingAuthority;
    try {
      preview = await firstValueFrom(this.status.tradingAuthority(shopId));
    } catch (error) {
      this.notice(attempt, refreshFailure(error));
      return false;
    }
    if (!this.current(attempt)) return true;
    const { payload } = attempt.held;
    const tillSession = this.till.enabled() ? this.till.selected()?.id : null;
    const needsRange = !attempt.previous.remainingCount || payload.sessionId !== attempt.invocation.sessionId ||
      payload.tillSessionId !== tillSession;
    if (!shouldReserveForRefresh(payload, preview, shopId, Date.now(), needsRange)) {
      if (!isShopTradingAuthority(preview, shopId) ||
          parseInstantNanos(preview.authorityVerifiedAt) !== parseInstantNanos(attempt.held.payload.authorityVerifiedAt)) {
        this.notice(attempt, 'unverified');
      }
      return false;
    }
    const { deviceId, fiscalYear } = attempt.previous;
    if (await this.leases.loadPendingClaim(shopId, deviceId, fiscalYear)) return true;
    if (!this.current(attempt) || !this.beginReservation(attempt)) return true;
    try {
      await this.reserveAndSync(attempt, preview.authorityVerifiedAt);
    } catch (error) {
      this.notice(attempt, refreshFailure(error));
    }
    return true;
  }

  private async capture(shopId: string): Promise<RefreshAttempt | null> {
    const invocation = this.authority.capture(shopId);
    const settings = this.settings.loadSettings(shopId);
    if (!invocation || !settings?.enabled || settings.enabledByUserId !== invocation.userId ||
        !settings.lastReservedLease || !this.canReserve(shopId) || !globalThis.navigator?.locks?.request) return null;
    const previous = await this.leases.loadLease(shopId, settings.deviceId, settings.lastReservedLease.fiscalYear);
    const stored = previous?.offlinePermit;
    if (!previous || !stored || previous.shopId !== shopId || previous.deviceId !== settings.deviceId ||
        previous.fiscalYear !== settings.lastReservedLease.fiscalYear || !isValidLeaseLicenceEvidence(previous.licenceEvidence) ||
        previous.leaseId !== stored.leaseId || previous.licenceEvidence.grantId !== stored.grantId ||
        previous.licenceEvidence.grantSha256 !== stored.grantSha256 || previous.deviceId !== stored.deviceId ||
        stored.shopId !== shopId || stored.userId !== invocation.userId) return null;
    const held = await verifyOfflinePermitSignature(stored.signedPermit, this.pins);
    return held && storedMatchesSigned(stored, held.keyId, held.payload) ? { previous, invocation, held } : null;
  }

  private canReserve(shopId: string): boolean {
    const role = this.auth.session()?.shops.find(shop => shop.shopId === shopId)?.role;
    return !!this.till.capability() && (role === 'Owner' || role === 'Manager') && (!this.till.enabled() || !!this.till.selected()?.id);
  }

  private current(attempt: RefreshAttempt): boolean {
    const { previous, invocation } = attempt;
    const settings = this.settings.loadSettings(invocation.shopId);
    return this.authority.isCurrent(invocation) && readRestriction(invocation.scope) === null && this.canReserve(invocation.shopId) &&
      settings?.enabled === true && settings.deviceId === previous.deviceId &&
      settings.enabledByUserId === invocation.userId && settings.lastReservedLease?.leaseId === previous.leaseId;
  }

  private notice(attempt: RefreshAttempt, notice: PermitRefreshNotice): void {
    if (this.current(attempt)) this.settings.updateSettings(attempt.invocation.shopId,
      current => ({ ...current, permitRefreshNotice: notice }));
  }

  /** Persist the retry before sending the POST. A lost response or tab reload may already have spent numbers. */
  private beginReservation(attempt: RefreshAttempt): boolean {
    const { shopId, leaseId } = attempt.previous;
    const retry = this.settings.loadSettings(shopId)?.permitRetry;
    const sameLease = retry?.leaseId === leaseId;
    if (sameLease && Date.now() < retry.after) return false;
    const failures = (sameLease ? retry.failures : 0) + 1;
    const delay = Math.min(30 * 60_000 * 2 ** (failures - 1), 12 * 3_600_000);
    this.settings.updateSettings(shopId, current => ({ ...current, permitRefreshNotice: 'unverified',
      permitRetry: { leaseId, failures, after: Date.now() + delay } }));
    return true;
  }

  private async reserveAndSync(attempt: RefreshAttempt, verifiedAt: string): Promise<void> {
    const { previous, invocation } = attempt;
    const blockSize = Math.min(200, Math.max(INVOICE_LEASE_RENEWAL_THRESHOLD, previous.remainingCount));
    const next = await firstValueFrom(this.sales.reserveInvoiceLease({ deviceId: previous.deviceId, blockSize }, invocation.scope));
    if (!this.current(attempt) || !isNewReservation(previous, next)) return;
    const binding = this.permits.bindingForLease(next, previous.shopId, invocation.userId, previous.deviceId);
    if (!binding) return;
    await this.snapshots.syncForShop(previous.shopId, binding, this.promote(attempt, next, binding, verifiedAt));
  }

  private promote(attempt: RefreshAttempt, next: InvoiceLeaseSnapshot, binding: OfflineLicencePermitBinding,
    verifiedAt: string): PromotePermitCandidate {
    const { previous, invocation } = attempt;
    const sameTill = () => (this.till.enabled() ? this.till.selected()?.id : null) === (binding.tillSessionId ?? null);
    return async (record, snapshotId, completedAt, complete) => {
      const stored = verifyDeliveredOfflineLicencePermit(record, { binding, snapshotId, snapshotCompletedAt: completedAt,
        sessionId: invocation.sessionId });
      const verified = stored && await verifyOfflinePermitSignature(stored.signedPermit, this.pins);
      if (!verified || !stored || !isFreshCandidate(attempt.held.payload, verified.payload, verifiedAt) ||
          parseInstantNanos(next.reservedAt)! > parseInstantNanos(stored.issuedAt)!) {
        if (record?.status === 'refused') this.notice(attempt, refusalNotice(record.refusalCode));
        throw new Error('Candidate permit was not freshly verified.');
      }
      const current = () => {
        const now = BigInt(Date.now()) * 1_000_000n;
        return this.current(attempt) && sameTill() && parseInstantNanos(stored.notBefore)! <= now &&
          now < parseInstantNanos(stored.validUntil)!;
      };
      const lease = await this.leases.loadLease(previous.shopId, previous.deviceId, previous.fiscalYear);
      const claim = await this.leases.loadPendingClaim(previous.shopId, previous.deviceId, previous.fiscalYear);
      if (claim || lease?.offlinePermit?.signedPermit !== attempt.held.signedPermit || !current()) {
        throw new Error('Previous issuing authority changed.');
      }
      // A crash between databases leaves a snapshot/permit mismatch. Both issuance guards refuse it.
      await complete();
      const swapped = await this.leases.replaceVerifiedLeaseUnderLock(previous, { ...next, offlinePermit: stored }, current);
      if (!swapped || !current()) throw new Error('Candidate lease was not adopted.');
      this.settings.updateSettings(next.shopId, settings => ({ ...settings, permitRetry: undefined,
        permitRefreshNotice: 'verified', permitAuthorityVerifiedAt: stored.authorityVerifiedAt,
        lastCompleteSnapshotAt: completedAt, lastOfflinePermit: toOfflinePermitDisplaySummary(stored),
        lastApiVerifiedAt: laterContact(settings, stored.deviceVerifiedAt),
        lastReservedLease: { leaseId: next.leaseId, fiscalYear: next.fiscalYear, expiresAt: next.expiresAt,
          remainingCount: next.remainingCount, licenceGrantId: stored.grantId } }));
      await this.permits.establishClockEvidence(stored, invocation.sessionId);
    };
  }
}

function laterContact(settings: OfflineSalesDeviceSettings, signedContact: string): string {
  const earlier = parseInstantNanos(settings.lastApiVerifiedAt);
  return earlier !== null && earlier >= parseInstantNanos(signedContact)! ? settings.lastApiVerifiedAt! : signedContact;
}

function refusalNotice(code?: string): PermitRefreshNotice {
  if (code === 'Licence.SecuritySuspended') return 'suspended';
  return ['Licence.Expired', 'Licence.TradingRestricted', 'Licence.PendingActivation'].includes(code ?? '') ?
    'restricted' : 'unverified';
}

function refreshFailure(error: unknown): PermitRefreshNotice {
  if (error instanceof TradingAuthorityReadError) {
    if (error.failure === 'securitySuspended') return 'suspended';
    return ['expired', 'restricted', 'pending'].includes(error.failure) ? 'restricted' : 'unverified';
  }
  return refusalNotice((error as { error?: { code?: string } } | null)?.error?.code);
}
