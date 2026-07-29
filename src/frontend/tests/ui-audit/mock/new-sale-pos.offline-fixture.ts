import type { Page } from '@playwright/test';

const SHOP_ID = 'ui-audit-shop';
const DEVICE_ID = 'ui-audit-offline-device';
const SNAPSHOT_ID = 'ui-audit-snapshot';
const COMPLETED_AT = '2099-01-01T00:00:00.000Z';

export async function seedOfflinePosSnapshot(page: Page): Promise<void> {
  await page.addInitScript(
    ({ shopId, deviceId, completedAt }) => {
      localStorage.setItem(`intelibill.offlineSales.deviceId.v1:${shopId}`, deviceId);
      localStorage.setItem(
        `intelibill.offlineSales.deviceSettings.v1:${shopId}:${deviceId}`,
        JSON.stringify({
          shopId,
          deviceId,
          label: 'UI audit offline device',
          enabled: true,
          enabledAt: completedAt,
          enabledByUserId: 'ui-audit-owner',
          enabledByUserName: 'UI Audit Owner',
          lastCompleteSnapshotAt: completedAt,
          lastApiVerifiedAt: completedAt,
          lastSnapshotWarningMarker: null,
          lastReservedLease: {
            leaseId: 'ui-audit-lease',
            fiscalYear: '2026-27',
            remainingCount: 5,
            expiresAt: '2099-12-31T00:00:00.000Z',
          },
        }),
      );
    },
    { shopId: SHOP_ID, deviceId: DEVICE_ID, completedAt: COMPLETED_AT },
  );

  await page.goto('/');
  await page.evaluate(
    async ({ shopId, deviceId, snapshotId, completedAt }) => {
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
          schemaVersion: 2,
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
            taxRatePercent: 18,
            taxIncluded: true,
            purchaseTaxIncluded: true,
            expiryDate: '2027-12-31',
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
            expiresAt: '2099-12-31T00:00:00.000Z',
          },
        });
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
      });
    },
    { shopId: SHOP_ID, deviceId: DEVICE_ID, snapshotId: SNAPSHOT_ID, completedAt: COMPLETED_AT },
  );
  await page.reload();
}
