import { Injectable } from '@angular/core';

import type { InvoiceLeaseLicenceEvidenceDto, InvoiceSellerProfile } from '../../features/sales/services/sale.models';
import type { StoredOfflineLicencePermit } from '../../features/sales/utils/offline-licence-permit';
import { underOfflineAuthorityLock } from './offline-issuance-lock';

export const INVOICE_LEASE_RENEWAL_THRESHOLD = 25;

export interface InvoiceLeaseSnapshot {
  readonly leaseId: string;
  readonly shopId: string;
  readonly deviceId: string;
  readonly fiscalYear: string;
  readonly prefix: string;
  readonly numberPadding: number;
  readonly rangeStart: number;
  readonly rangeEnd: number;
  readonly nextNumber: number;
  readonly remainingCount: number;
  readonly reservedAt: string;
  readonly expiresAt: string;
  /**
   * The seller identity the server froze on this lease; offline documents are issued under it. Absent from leases
   * stored before documents were issued offline, which cannot issue until the device is enabled again.
   */
  readonly sellerProfile?: InvoiceSellerProfile | null;
  /** Versioned reference to the server-side grant frozen when this lease was reserved. */
  readonly licenceEvidence?: InvoiceLeaseLicenceEvidenceDto | null;
  /** Latest verified offline licence permit bound to this lease. */
  readonly offlinePermit?: StoredOfflineLicencePermit | null;
}

export interface InvoiceLeaseConsumptionResult {
  readonly invoiceNumber: string;
  readonly lease: InvoiceLeaseSnapshot;
  readonly remainingCount: number;
}

/** The exact stored authority a number may be taken under; anything else in the record refuses the allocation. */
export interface InvoiceNumberGuard {
  readonly leaseId: string;
  readonly nextNumber: number;
  readonly signedPermit: string;
  readonly clientSaleId: string;
}

/** A number taken for one sale and not yet settled by a durable queue row. */
export interface InvoiceNumberClaim {
  readonly leaseId: string;
  readonly number: number;
  readonly clientSaleId: string;
}

interface InvoiceLeaseRecord {
  readonly key: string;
  readonly shopId: string;
  readonly deviceId: string;
  readonly fiscalYear: string;
  readonly lease: InvoiceLeaseSnapshot;
  readonly updatedAt: string;
  /** Present from allocation until the sale's queue row is durable or the number is given back. */
  readonly claim?: InvoiceNumberClaim | null;
}

export class InvoiceLeaseChangedError extends Error {
  constructor(message = 'Invoice lease changed since it was verified.') {
    super(message);
    this.name = 'InvoiceLeaseChangedError';
  }
}

export class InvoiceLeaseExpiredError extends Error {
  constructor(message = 'Invoice lease has expired.') {
    super(message);
    this.name = 'InvoiceLeaseExpiredError';
  }
}

export class InvoiceLeaseExhaustedError extends Error {
  constructor(message = 'Invoice lease is exhausted.') {
    super(message);
    this.name = 'InvoiceLeaseExhaustedError';
  }
}

export class InvoiceLeaseNotFoundError extends Error {
  constructor(message = 'Invoice lease was not found.') {
    super(message);
    this.name = 'InvoiceLeaseNotFoundError';
  }
}

@Injectable({ providedIn: 'root' })
export class InvoiceLeaseIndexedDbService {
  private readonly databaseName = 'intelibill-invoice-leases';
  private readonly databaseVersion = 1;
  private readonly storeName = 'invoice-leases';

  async saveLease(lease: InvoiceLeaseSnapshot): Promise<void> {
    if (!lease?.shopId || typeof indexedDB === 'undefined') {
      return;
    }

    // Replacing a lease waits for an issuance in flight in any tab, so a number is never taken from a swapped lease.
    await underOfflineAuthorityLock(lease.shopId, () => this.putLease(lease));
  }

  private async putLease(lease: InvoiceLeaseSnapshot): Promise<void> {
    const database = await this.openDatabase();
    const record: InvoiceLeaseRecord = {
      key: this.buildKey(lease.shopId, lease.deviceId, lease.fiscalYear),
      shopId: lease.shopId,
      deviceId: lease.deviceId,
      fiscalYear: lease.fiscalYear,
      lease: this.normalizeLease(lease),
      updatedAt: new Date().toISOString(),
    };

    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readwrite');
      const request = transaction.objectStore(this.storeName).put(record);

      request.onsuccess = () => resolve();
      request.onerror = () => reject(request.error);
    });
  }

  async loadLease(shopId: string, deviceId: string, fiscalYear: string): Promise<InvoiceLeaseSnapshot | null> {
    if (!shopId || !deviceId || !fiscalYear || typeof indexedDB === 'undefined') {
      return null;
    }

    const database = await this.openDatabase();
    const record = await new Promise<InvoiceLeaseRecord | undefined>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readonly');
      const request = transaction.objectStore(this.storeName).get(this.buildKey(shopId, deviceId, fiscalYear));

      request.onsuccess = () => resolve(request.result as InvoiceLeaseRecord | undefined);
      request.onerror = () => reject(request.error);
    });

    return record ? this.normalizeLease(record.lease) : null;
  }

  /**
   * Stores a verified permit inside its matching lease record in one read-modify-write transaction.
   * Refuses mismatched lease/grant evidence and keeps the later issuedAt when a newer permit is already stored.
   */
  async attachPermit(
    shopId: string,
    deviceId: string,
    fiscalYear: string,
    permit: StoredOfflineLicencePermit,
  ): Promise<StoredOfflineLicencePermit | null> {
    if (!shopId || !deviceId || !fiscalYear || !permit || typeof indexedDB === 'undefined') {
      return null;
    }

    return await underOfflineAuthorityLock(shopId, () => this.putPermit(shopId, deviceId, fiscalYear, permit));
  }

  private async putPermit(
    shopId: string,
    deviceId: string,
    fiscalYear: string,
    permit: StoredOfflineLicencePermit,
  ): Promise<StoredOfflineLicencePermit | null> {
    const database = await this.openDatabase();
    return await new Promise<StoredOfflineLicencePermit | null>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readwrite');
      const store = transaction.objectStore(this.storeName);
      const request = store.get(this.buildKey(shopId, deviceId, fiscalYear));

      request.onerror = () => reject(request.error);
      request.onsuccess = () => {
        const record = request.result as InvoiceLeaseRecord | undefined;
        if (!record) {
          resolve(null);
          return;
        }

        const lease = this.normalizeLease(record.lease);
        if (
          lease.leaseId !== permit.leaseId ||
          lease.licenceEvidence?.version !== 1 ||
          lease.licenceEvidence.grantId !== permit.grantId ||
          lease.licenceEvidence.grantSha256 !== permit.grantSha256
        ) {
          resolve(null);
          return;
        }

        // Canonical UTC microsecond strings sort identically to chronological order.
        if (lease.offlinePermit && permit.issuedAt < lease.offlinePermit.issuedAt) {
          resolve(null);
          return;
        }

        const updatedRecord: InvoiceLeaseRecord = {
          ...record,
          lease: this.normalizeLease({ ...lease, offlinePermit: permit }),
          updatedAt: new Date().toISOString(),
        };
        const putRequest = store.put(updatedRecord);
        putRequest.onerror = () => reject(putRequest.error);
        putRequest.onsuccess = () => resolve(permit);
      };
    });
  }

  /**
   * Takes the next number. With a `guard`, the stored lease, cursor and permit bytes must be exactly the ones the
   * caller verified, the claim is recorded in the same transaction, and the answer waits for that transaction to
   * be durable.
   */
  async consumeNextInvoiceNumber(
    shopId: string,
    deviceId: string,
    fiscalYear: string,
    guard?: InvoiceNumberGuard,
  ): Promise<InvoiceLeaseConsumptionResult> {
    if (!shopId || !deviceId || !fiscalYear || typeof indexedDB === 'undefined') {
      throw new InvoiceLeaseNotFoundError();
    }

    const database = await this.openDatabase();
    return await new Promise<InvoiceLeaseConsumptionResult>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readwrite');
      const store = transaction.objectStore(this.storeName);
      const request = store.get(this.buildKey(shopId, deviceId, fiscalYear));

      request.onerror = () => reject(request.error);
      request.onsuccess = () => {
        const record = request.result as InvoiceLeaseRecord | undefined;
        if (!record) {
          reject(new InvoiceLeaseNotFoundError());
          return;
        }

        const lease = this.normalizeLease(record.lease);
        if (
          guard &&
          (lease.leaseId !== guard.leaseId ||
            lease.nextNumber !== guard.nextNumber ||
            lease.offlinePermit?.signedPermit !== guard.signedPermit)
        ) {
          reject(new InvoiceLeaseChangedError());
          return;
        }

        if (this.isExpired(lease)) {
          reject(new InvoiceLeaseExpiredError());
          return;
        }

        if (lease.nextNumber > lease.rangeEnd) {
          reject(new InvoiceLeaseExhaustedError());
          return;
        }

        const invoiceNumber = this.formatInvoiceNumber(lease, lease.nextNumber);
        const updatedLease = this.normalizeLease({
          ...lease,
          nextNumber: lease.nextNumber + 1,
        });

        const updatedRecord: InvoiceLeaseRecord = {
          ...record,
          lease: updatedLease,
          updatedAt: new Date().toISOString(),
          ...(guard
            ? { claim: { leaseId: lease.leaseId, number: lease.nextNumber, clientSaleId: guard.clientSaleId } }
            : {}),
        };
        const consumed = { invoiceNumber, lease: updatedLease, remainingCount: updatedLease.remainingCount };

        const putRequest = store.put(updatedRecord);
        putRequest.onerror = () => reject(putRequest.error);
        if (guard) {
          transaction.oncomplete = () => resolve(consumed);
          transaction.onabort = () => reject(transaction.error ?? putRequest.error);
        } else {
          putRequest.onsuccess = () => resolve(consumed);
        }
      };
    });
  }

  /** The number claim left on the record, if any: an issuance that was interrupted before it settled. */
  async loadPendingClaim(shopId: string, deviceId: string, fiscalYear: string): Promise<InvoiceNumberClaim | null> {
    if (!shopId || !deviceId || !fiscalYear || typeof indexedDB === 'undefined') return null;
    const database = await this.openDatabase();
    return await new Promise<InvoiceNumberClaim | null>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readonly');
      const request = transaction.objectStore(this.storeName).get(this.buildKey(shopId, deviceId, fiscalYear));
      request.onsuccess = () => resolve((request.result as InvoiceLeaseRecord | undefined)?.claim ?? null);
      request.onerror = () => reject(request.error);
    });
  }

  /**
   * Clears the claim of one sale, leaving the cursor advanced: the number stays used. Called once the sale's queue
   * row is durable, and for an interrupted claim, whose number is never handed out again.
   */
  async settleClaim(shopId: string, deviceId: string, fiscalYear: string, clientSaleId: string): Promise<void> {
    if (!shopId || !deviceId || !fiscalYear || typeof indexedDB === 'undefined') return;
    const database = await this.openDatabase();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readwrite');
      const store = transaction.objectStore(this.storeName);
      const request = store.get(this.buildKey(shopId, deviceId, fiscalYear));
      request.onerror = () => reject(request.error);
      request.onsuccess = () => {
        const record = request.result as InvoiceLeaseRecord | undefined;
        if (record?.claim?.clientSaleId === clientSaleId) store.put({ ...record, claim: null } satisfies InvoiceLeaseRecord);
      };
      transaction.oncomplete = () => resolve();
      transaction.onabort = () => reject(transaction.error);
    });
  }

  async rollbackConsumedInvoiceNumber(
    shopId: string,
    deviceId: string,
    fiscalYear: string,
    consumedLeaseNextNumber: number,
    claim?: InvoiceNumberClaim,
  ): Promise<InvoiceLeaseSnapshot | null> {
    if (!shopId || !deviceId || !fiscalYear || typeof indexedDB === 'undefined') {
      throw new InvoiceLeaseNotFoundError();
    }

    const database = await this.openDatabase();
    return await new Promise<InvoiceLeaseSnapshot | null>((resolve, reject) => {
      const transaction = database.transaction(this.storeName, 'readwrite');
      const store = transaction.objectStore(this.storeName);
      const request = store.get(this.buildKey(shopId, deviceId, fiscalYear));

      request.onerror = () => reject(request.error);
      request.onsuccess = () => {
        const record = request.result as InvoiceLeaseRecord | undefined;
        if (!record) {
          reject(new InvoiceLeaseNotFoundError());
          return;
        }

        const lease = this.normalizeLease(record.lease);
        if (lease.nextNumber !== consumedLeaseNextNumber || consumedLeaseNextNumber <= lease.rangeStart) {
          resolve(lease);
          return;
        }
        // Only the exact lease, cursor and claim of the refused sale are restored; anything else is left as found.
        if (
          claim &&
          (lease.leaseId !== claim.leaseId ||
            record.claim?.leaseId !== claim.leaseId ||
            record.claim.number !== claim.number ||
            record.claim.clientSaleId !== claim.clientSaleId)
        ) {
          resolve(lease);
          return;
        }

        const restoredLease = this.normalizeLease({
          ...lease,
          nextNumber: consumedLeaseNextNumber - 1,
        });

        const putRequest = store.put({
          ...record,
          lease: restoredLease,
          updatedAt: new Date().toISOString(),
          ...(claim ? { claim: null } : {}),
        } satisfies InvoiceLeaseRecord);
        putRequest.onerror = () => reject(putRequest.error);
        putRequest.onsuccess = () => resolve(restoredLease);
      };
    });
  }

  getRemainingCount(lease: InvoiceLeaseSnapshot): number {
    return Math.max(lease.rangeEnd - lease.nextNumber + 1, 0);
  }

  isBelowRenewalThreshold(
    lease: InvoiceLeaseSnapshot,
    threshold: number = INVOICE_LEASE_RENEWAL_THRESHOLD
  ): boolean {
    return this.getRemainingCount(lease) < threshold;
  }

  private normalizeLease(lease: InvoiceLeaseSnapshot): InvoiceLeaseSnapshot {
    return {
      ...lease,
      remainingCount: this.getRemainingCount(lease),
    };
  }

  private isExpired(lease: InvoiceLeaseSnapshot): boolean {
    const expiryMs = Date.parse(lease.expiresAt);
    return Number.isNaN(expiryMs) || expiryMs <= Date.now();
  }

  private formatInvoiceNumber(lease: InvoiceLeaseSnapshot, number: number): string {
    const padded = number.toString().padStart(lease.numberPadding, '0');
    return `${lease.prefix}${padded}`;
  }

  private buildKey(shopId: string, deviceId: string, fiscalYear: string): string {
    return `${shopId}::${deviceId}::${fiscalYear}`;
  }

  private async openDatabase(): Promise<IDBDatabase> {
    return await new Promise((resolve, reject) => {
      const request = indexedDB.open(this.databaseName, this.databaseVersion);

      request.onupgradeneeded = () => {
        const database = request.result;
        if (!database.objectStoreNames.contains(this.storeName)) {
          database.createObjectStore(this.storeName, { keyPath: 'key' });
        }
      };

      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }
}
