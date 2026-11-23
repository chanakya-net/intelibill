import { TillContextService } from '../../tills/services/till-context.service';
import { Injectable, inject } from '@angular/core';

import { AuthService } from '../../../core/auth/auth.service';
import { ShopContextService } from '../../../core/context/shop-context.service';
import type { ShopScope } from '../../../core/context/shop-scope';
import {
  InvoiceLeaseIndexedDbService,
  type InvoiceLeaseSnapshot,
} from '../../../core/storage/invoice-lease-indexeddb.service';
import { OfflinePermitClockIndexedDbService } from '../../../core/storage/offline-permit-clock-indexeddb.service';
import {
  isValidLeaseLicenceEvidence,
  parseInstantNanos,
  type OfflineLicensePermitPayload,
  type StoredOfflineLicencePermit,
} from '../utils/offline-licence-permit';
import { OFFLINE_PERMIT_PUBLIC_KEYS, verifyOfflinePermitSignature } from '../utils/offline-permit-signature';
import type { OfflineSaleLicenceEvidence } from './offline-sale-core.types';

export type OfflineIssuanceRefusal =
  /** No stored permit, or one whose signature, canonical form or scope does not hold for this sale. */
  | 'OFFLINE_PERMIT_INVALID'
  /** A genuine in-scope permit whose signed window does not contain the sale. */
  | 'OFFLINE_PERMIT_EXPIRED'
  /** The device clocks disagree with their own earlier readings, or were never verified online. */
  | 'OFFLINE_CLOCK_INCONSISTENT';

/** Who asked, captured synchronously before the first await of a checkout. */
export interface OfflineIssuanceInvocation {
  readonly scope: ShopScope;
  readonly epoch: number;
  readonly userId: string;
  readonly shopId: string;
  readonly sessionId: string;
}

export interface OfflineIssuanceQuestion {
  readonly shopId: string;
  readonly deviceId: string;
  readonly fiscalYear: string;
  readonly soldAt: string;
  readonly snapshotId: string;
  readonly snapshotCompletedAt: string;
  readonly tillSessionId: string | null;
  /** Second guard only: the lease must still be the one the number was taken from, one number further on. */
  readonly expected?: { readonly leaseId: string; readonly signedPermit: string; readonly nextNumber: number };
}

export interface OfflineIssuanceGrant {
  readonly ok: true;
  readonly lease: InvoiceLeaseSnapshot;
  readonly signedPermit: string;
  readonly evidence: OfflineSaleLicenceEvidence;
  /** The signed deadline, as signed. */
  readonly validUntil: string;
  readonly tillSessionId: string | null;
  readonly notBeforeNanos: bigint;
  readonly validUntilNanos: bigint;
}

export type OfflineIssuanceDecision =
  | OfflineIssuanceGrant
  | {
      readonly ok: false;
      readonly reason: OfflineIssuanceRefusal;
      /** Set only for a verified, in-scope permit: the signed instant the device must reconnect by. */
      readonly validUntil?: string;
    };

const NANOS_PER_MS = 1_000_000n;
const INVALID = { ok: false, reason: 'OFFLINE_PERMIT_INVALID' } as const;

/** Every stored display field must repeat what the signature covers; an edited record is refused, never trusted. */
function storedMatchesSigned(stored: StoredOfflineLicencePermit, keyId: string, signed: OfflineLicensePermitPayload): boolean {
  const members = [
    'permitId', 'grantId', 'grantSha256', 'leaseId', 'snapshotId', 'shopId', 'userId', 'sessionId', 'deviceId',
    'authorityVerifiedAt', 'deviceVerifiedAt', 'snapshotCompletedAt', 'issuedAt', 'notBefore', 'validUntil',
    'limitingReason', 'tillSessionId',
  ] as const;
  return stored.version === 1 && stored.keyId === keyId && members.every((member) => stored[member] === signed[member]);
}

/**
 * Decides whether this device may issue one offline invoice now. It trusts only the build's pinned public keys and
 * the bytes the server signed; the stored summary and display fields authorize nothing.
 */
@Injectable({ providedIn: 'root' })
export class OfflineIssuanceAuthorityService {
  private readonly till = inject(TillContextService, { optional: true });
  private readonly auth = inject(AuthService);
  private readonly context = inject(ShopContextService);
  private readonly leaseDb = inject(InvoiceLeaseIndexedDbService);
  private readonly clock = inject(OfflinePermitClockIndexedDbService);
  private readonly publicKeys = inject(OFFLINE_PERMIT_PUBLIC_KEYS);

  /** Synchronous: call before the first await so a later scope change cannot be mistaken for the caller. */
  capture(shopId: string): OfflineIssuanceInvocation | null {
    const scope = this.context.scope();
    const session = this.auth.session();
    const userId = session?.user?.id;
    const sessionId = session?.sessionId;
    if (!scope || !userId || !sessionId || scope.shopId !== shopId || scope.userId !== userId) return null;
    if (session.activeShopId !== shopId || this.context.dataRestricted()) return null;
    return { scope, epoch: this.context.epoch(), userId, shopId, sessionId };
  }

  /** Synchronous: the same tab epoch, owner, session and unrestricted shop as when the checkout began. */
  isCurrent(invocation: OfflineIssuanceInvocation): boolean {
    const session = this.auth.session();
    return (
      this.context.epoch() === invocation.epoch &&
      this.context.isCurrent(invocation.scope) &&
      !this.context.dataRestricted() &&
      session?.user?.id === invocation.userId &&
      session.activeShopId === invocation.shopId &&
      session.sessionId === invocation.sessionId
    );
  }

  private tillMatches(expected: string | null): boolean {
    if (!this.till) return true;
    if (!this.till.capability()) return false;
    if (!this.till.enabled()) return expected === null;
    return expected !== null && this.till.selected()?.id === expected;
  }

  /** Synchronous owner, till and time check immediately before allocation or queue insertion. */
  refusalNow(invocation: OfflineIssuanceInvocation, grant: OfflineIssuanceGrant, soldAt: string) {
    if (!this.isCurrent(invocation) || !this.tillMatches(grant.tillSessionId)) return INVALID;
    if (!this.insideWindow(grant.notBeforeNanos, grant.validUntilNanos, soldAt)) {
      return { ok: false, reason: 'OFFLINE_PERMIT_EXPIRED', validUntil: grant.validUntil } as const;
    }
    return null;
  }

  async authorize(invocation: OfflineIssuanceInvocation, question: OfflineIssuanceQuestion): Promise<OfflineIssuanceDecision> {
    if (!this.isCurrent(invocation) || question.shopId !== invocation.shopId || !this.tillMatches(question.tillSessionId)) return INVALID;

    const lease = await this.leaseDb.loadLease(question.shopId, question.deviceId, question.fiscalYear);
    const stored = lease?.offlinePermit;
    if (!lease || !stored || !this.leaseMatches(lease, question)) return INVALID;

    const verified = await verifyOfflinePermitSignature(stored.signedPermit, this.publicKeys);
    if (!verified || !storedMatchesSigned(stored, verified.keyId, verified.payload)) return INVALID;
    const signed = verified.payload;
    if (!this.scopeMatches(signed, invocation, lease, question)) return INVALID;

    const notBeforeNanos = parseInstantNanos(signed.notBefore);
    const validUntilNanos = parseInstantNanos(signed.validUntil);
    const leaseExpires = parseInstantNanos(lease.expiresAt);
    if (notBeforeNanos === null || validUntilNanos === null || leaseExpires === null || validUntilNanos > leaseExpires) {
      return INVALID;
    }

    const owner = { shopId: signed.shopId, deviceId: signed.deviceId, userId: signed.userId, sessionId: signed.sessionId };
    if ((await this.clock.evaluate(owner, signed.permitId)) !== 'consistent') {
      return { ok: false, reason: 'OFFLINE_CLOCK_INCONSISTENT' };
    }
    if (!this.isCurrent(invocation) || !this.tillMatches(question.tillSessionId)) return INVALID;
    if (!this.insideWindow(notBeforeNanos, validUntilNanos, question.soldAt)) {
      return { ok: false, reason: 'OFFLINE_PERMIT_EXPIRED', validUntil: signed.validUntil };
    }

    return {
      ok: true,
      lease,
      signedPermit: verified.signedPermit,
      validUntil: signed.validUntil,
      tillSessionId: signed.tillSessionId,
      notBeforeNanos,
      validUntilNanos,
      evidence: {
        version: 1,
        permitId: signed.permitId,
        grantId: signed.grantId,
        grantSha256: signed.grantSha256,
        leaseId: signed.leaseId,
        snapshotId: signed.snapshotId,
        keyId: verified.keyId,
        signedPermit: verified.signedPermit,
      },
    };
  }

  private leaseMatches(lease: InvoiceLeaseSnapshot, question: OfflineIssuanceQuestion): boolean {
    const expected = question.expected;
    return (
      lease.shopId === question.shopId &&
      lease.deviceId === question.deviceId &&
      lease.fiscalYear === question.fiscalYear &&
      isValidLeaseLicenceEvidence(lease.licenceEvidence) &&
      (!expected ||
        (lease.leaseId === expected.leaseId &&
          lease.nextNumber === expected.nextNumber &&
          lease.offlinePermit?.signedPermit === expected.signedPermit))
    );
  }

  private scopeMatches(
    signed: OfflineLicensePermitPayload,
    invocation: OfflineIssuanceInvocation,
    lease: InvoiceLeaseSnapshot,
    question: OfflineIssuanceQuestion,
  ): boolean {
    const completed = parseInstantNanos(question.snapshotCompletedAt);
    return (
      signed.shopId === invocation.shopId &&
      signed.userId === invocation.userId &&
      signed.sessionId === invocation.sessionId &&
      signed.deviceId === question.deviceId &&
      signed.leaseId === lease.leaseId &&
      signed.grantId === lease.licenceEvidence?.grantId &&
      signed.grantSha256 === lease.licenceEvidence?.grantSha256 &&
      signed.snapshotId === question.snapshotId &&
      completed !== null &&
      completed === parseInstantNanos(signed.snapshotCompletedAt) &&
      signed.tillSessionId === question.tillSessionId
    );
  }

  /** Both the sale's own instant and the moment of issuing must lie in `[notBefore, validUntil)`. */
  private insideWindow(notBefore: bigint, validUntil: bigint, soldAt: string): boolean {
    const sold = parseInstantNanos(soldAt);
    const now = BigInt(Date.now()) * NANOS_PER_MS;
    return sold !== null && sold >= notBefore && sold < validUntil && now >= notBefore && now < validUntil;
  }
}
