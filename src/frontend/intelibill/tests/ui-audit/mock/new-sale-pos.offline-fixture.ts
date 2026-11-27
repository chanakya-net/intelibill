import type { Page } from '@playwright/test';
import type { InvoiceSellerProfile } from '../../../src/app/features/sales/services/sale.models';

const SHOP_ID = 'ui-audit-shop';
const DEVICE_ID = 'ui-audit-offline-device';
const SNAPSHOT_ID = 'ui-audit-snapshot';
const COMPLETED_AT = new Date(Date.now() - 60_000).toISOString();
const EXPIRES_AT = new Date(Date.now() + 3_600_000).toISOString();
const SELLER_PROFILE: InvoiceSellerProfile = {
  seller: {
    legalName: 'UI Audit Shop', tradeName: 'UI Audit Shop', address: 'Audit Lane, Bengaluru',
    stateCode: '29', stateName: 'Karnataka', gstin: null, taxRegime: 'Unregistered',
  },
  eInvoiceStatus: 'BelowThreshold',
  seriesCode: 'A',
};

export function offlinePosLocalStorageEntries(): Readonly<Record<string, string>> {
  return {
    [`intelibill.offlineSales.deviceId.v1:${SHOP_ID}`]: DEVICE_ID,
    [`intelibill.offlineSales.deviceSettings.v1:${SHOP_ID}:${DEVICE_ID}`]: JSON.stringify({
      shopId: SHOP_ID,
      deviceId: DEVICE_ID,
      label: 'UI audit offline device',
      enabled: true,
      enabledAt: COMPLETED_AT,
      enabledByUserId: 'ui-audit-owner',
      enabledByUserName: 'UI Audit Owner',
      lastCompleteSnapshotAt: COMPLETED_AT,
      lastApiVerifiedAt: COMPLETED_AT,
      lastSnapshotWarningMarker: null,
      lastReservedLease: {
        leaseId: 'ui-audit-lease',
        fiscalYear: '2026-27',
        remainingCount: 5,
        expiresAt: EXPIRES_AT,
      },
    }),
  };
}

export async function seedOfflinePosSnapshot(page: Page): Promise<void> {
  await page.goto('/');
  await page.evaluate(
    async ({ shopId, deviceId, snapshotId, completedAt, expiresAt, sellerProfile }) => {
      const openDatabase = (
        name: string,
        version: number,
        configure: (database: IDBDatabase) => void,
      ) =>
        new Promise<IDBDatabase>((resolve, reject) => {
          const request = indexedDB.open(name, version);
          request.onupgradeneeded = () => configure(request.result);
          request.onsuccess = () => resolve(request.result);
          request.onerror = () => reject(request.error);
        });
      const snapshotStores = [
        ['snapshot-attempts', 'snapshotId'],
        ['shop-pointers', 'shopId'],
        ['batches', 'key'],
        ['customers', 'key'],
        ['discount-rules', 'key'],
        ['active-leases', 'key'],
        ['services', 'key'],
      ] as const;
      const snapshotDb = await openDatabase('intelibill-offline-sales-snapshot', 2, (database) => {
        snapshotStores.forEach(([name, keyPath]) => {
          if (!database.objectStoreNames.contains(name))
            database.createObjectStore(name, { keyPath });
        });
      });
      await new Promise<void>((resolve, reject) => {
        const tx = snapshotDb.transaction(
          snapshotStores.map(([name]) => name),
          'readwrite',
        );
        tx.objectStore('snapshot-attempts').put({
          snapshotId,
          shopId,
          schemaVersion: 4,
          businessTimeZoneId: 'Asia/Kolkata',
          catalogVersion: 1,
          startedAt: completedAt,
          completedAt,
          status: 'complete',
        });
        tx.objectStore('shop-pointers').put({
          shopId,
          usableSnapshotId: snapshotId,
          usableCompletedAt: completedAt,
          lastAttemptSnapshotId: snapshotId,
          lastAttemptStartedAt: completedAt,
          lastAttemptStatus: 'complete',
        });
        tx.objectStore('batches').put({
          key: `${snapshotId}::offline-batch`,
          snapshotId,
          shopId,
          entityId: 'offline-batch',
          writtenAt: completedAt,
          entity: {
            batchId: 'offline-batch',
            itemId: 'offline-item',
            itemName: 'Offline catalog product with a long localized audit name',
            barcode: 'OFFLINE-001',
            uom: 'Each',
            hsnCode: '1234',
            batchNumber: 'OFFLINE-BATCH-001',
            quantity: 25,
            costPrice: 75,
            mrp: 150,
            salesPrice: 125,
            taxRatePercent: 0,
            taxIncluded: false,
            purchaseTaxIncluded: false,
            expiryDate: null,
            manufacturingDate: null,
            expiryEvidenceId: 'offline-expiry-evidence',
            effectivePolicy: {
              version: 1, mode: 'Ignore', expiryRequired: false,
              sellThroughExpiryDate: true, warningDays: 7, overrideAllowed: false,
            },
            physicalQuantity: 25,
            quarantinedQuantity: 0,
            availableQuantity: 25,
            batchVersion: 1,
            expiryCapturedAt: completedAt,
            expiryEvidenceExpiresAt: expiresAt,
          },
        });
        tx.objectStore('customers').put({
          key: `${snapshotId}::offline-customer`,
          snapshotId,
          shopId,
          entityId: 'offline-customer',
          writtenAt: completedAt,
          entity: {
            customerId: 'offline-customer',
            name: 'Offline customer',
            phoneNumber: '+919999999999',
          },
        });
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
      });
      const leaseDb = await openDatabase('intelibill-invoice-leases', 1, (database) => {
        if (!database.objectStoreNames.contains('invoice-leases'))
          database.createObjectStore('invoice-leases', { keyPath: 'key' });
      });
      await new Promise<void>((resolve, reject) => {
        const tx = leaseDb.transaction('invoice-leases', 'readwrite');
        tx.objectStore('invoice-leases').put({
          key: `${shopId}::${deviceId}::2026-27`,
          shopId,
          deviceId,
          fiscalYear: '2026-27',
          updatedAt: completedAt,
          lease: {
            leaseId: 'ui-audit-lease',
            shopId,
            deviceId,
            fiscalYear: '2026-27',
            prefix: 'OFF-',
            numberPadding: 4,
            rangeStart: 1,
            rangeEnd: 5,
            nextNumber: 1,
            remainingCount: 5,
            reservedAt: completedAt,
            expiresAt,
            sellerProfile,
          },
        });
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
      });
    },
    { shopId: SHOP_ID, deviceId: DEVICE_ID, snapshotId: SNAPSHOT_ID, completedAt: COMPLETED_AT,
      expiresAt: EXPIRES_AT, sellerProfile: SELLER_PROFILE },
  );
  await page.reload();
}
