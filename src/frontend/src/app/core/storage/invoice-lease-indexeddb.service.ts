import { Injectable } from '@angular/core';

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
}

export interface InvoiceLeaseConsumptionResult {
  readonly invoiceNumber: string;
  readonly lease: InvoiceLeaseSnapshot;
  readonly remainingCount: number;
}

interface InvoiceLeaseRecord {
  readonly key: string;
  readonly shopId: string;
  readonly deviceId: string;
  readonly fiscalYear: string;
  readonly lease: InvoiceLeaseSnapshot;
  readonly updatedAt: string;
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

  async consumeNextInvoiceNumber(
    shopId: string,
    deviceId: string,
    fiscalYear: string
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
        };

        const putRequest = store.put(updatedRecord);
        putRequest.onerror = () => reject(putRequest.error);
        putRequest.onsuccess = () =>
          resolve({
            invoiceNumber,
            lease: updatedLease,
            remainingCount: updatedLease.remainingCount,
          });
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
