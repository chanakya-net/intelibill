import { expect, type Page, type Route } from '@playwright/test';

import { sha256Hex } from './legacy-queue-recovery-fixture';
import { MSG_CLOCK, submit, triggerReconnect } from './licensing-offline-fixtures';
import {
  DEVICE_ID,
  canonicalInstant,
  durableState,
  readClockRow,
  readQueueRows,
  type OfflineSaleFixture,
} from './offline-permit-finalization-fixture';
import { SHOP_A } from './purchase-receiving-server';

const SYNC_ROUTE = '**/api/sales/offline-sync';
const UNSYNCED = ['Pending', 'Failed', 'Syncing'];
const RETRY_SETTLE_MS = 3_000;

type DurableState = Awaited<ReturnType<typeof durableState>>;

/** A refused checkout shows the clock notice and leaves cursor, claim, queue and shadow stock as they were. */
export async function expectClockRefusal(page: Page, before: DurableState): Promise<void> {
  await submit(page).click();
  await expect(page.getByText(MSG_CLOCK, { exact: false }).first()).toBeVisible();
  expect(await durableState(page)).toEqual(before);
}

/** Ages the last complete snapshot so the next `online` event asks the server for a later signed permit. */
export async function forceVerifiedRefresh(page: Page, fixture: OfflineSaleFixture): Promise<void> {
  await expect(async () => {
    await page.evaluate(({ shopId, deviceId }) => {
      const key = `intelibill.offlineSales.deviceSettings.v1:${shopId}:${deviceId}`;
      const current = JSON.parse(localStorage.getItem(key) ?? 'null');
      current.lastCompleteSnapshotAt = '2020-01-01T00:00:00.000Z';
      localStorage.setItem(key, JSON.stringify(current));
      localStorage.removeItem('intelibill.offlineSales.offlinePackCycle.v1');
      window.dispatchEvent(new Event('online'));
    }, { shopId: SHOP_A, deviceId: DEVICE_ID });
    await page.waitForTimeout(1_500);
    expect(fixture.delivered.length).toBeGreaterThanOrEqual(2);
  }).toPass({ timeout: 60_000 });
}

/** Answers authority previews with a verification older than the held permit and counts the polls. */
export async function routeStaleAuthority(page: Page, heldAuthorityVerifiedAt: string) {
  const state = { polls: 0 };
  const handler = (route: Route) => {
    state.polls += 1;
    const staleAt = Date.parse(heldAuthorityVerifiedAt) - 1_000;
    return route.fulfill({
      json: {
        schemaVersion: 1, shopId: SHOP_A, enforced: true,
        authorityVerifiedAt: canonicalInstant(staleAt),
        validUntil: canonicalInstant(staleAt + 86_400_000),
      },
    });
  };
  await page.route('**/trading-authority*', handler);
  return { state, remove: () => page.unroute('**/trading-authority*', handler) };
}

/** Accepts every uploaded sale as created and keeps the transmitted wire sales. */
export async function routeAcceptedUploads(page: Page): Promise<Record<string, unknown>[]> {
  const uploaded: Record<string, unknown>[] = [];
  await page.route(SYNC_ROUTE, (route) => {
    const body = route.request().postDataJSON() as { sales: Record<string, unknown>[] };
    uploaded.push(...body.sales);
    return route.fulfill({
      json: {
        results: body.sales.map((s) => ({
          clientSaleId: s['clientSaleId'], status: 'created', saleId: '99999999-9999-4999-8999-999999999991',
          invoiceNumber: s['invoiceNumber'], warnings: [], errors: [],
        })),
      },
    });
  });
  return uploaded;
}

async function readOnlyQueuedRow(page: Page) {
  const rows = await readQueueRows(page);
  expect(rows).toHaveLength(1);
  return { status: rows[0].status, invoiceNumber: rows[0].invoiceNumber, sha256: sha256Hex(JSON.stringify(rows[0].payload)) };
}

/**
 * A security-suspended shop is refused on upload (403 Licence.SecuritySuspended). The queued invoice must stay on
 * the device, unsent and byte-identical, across the refused reconnect and a reload.
 */
export async function expectQueuePreservedWhenUploadBarred(page: Page, origin: string) {
  const queued = await readOnlyQueuedRow(page);
  expect(UNSYNCED).toContain(queued.status);

  const refused: string[] = [];
  await page.route(SYNC_ROUTE, (route) => {
    const body = route.request().postDataJSON() as { sales: Record<string, unknown>[] };
    refused.push(...body.sales.map((s) => String(s['invoiceNumber'])));
    return route.fulfill({
      status: 403,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.4', title: 'Forbidden', status: 403,
        code: 'Licence.SecuritySuspended', traceId: '00-p0509-security-suspended-00',
      }),
    });
  });

  await page.context().setOffline(false);
  await triggerReconnect(page);
  await expect.poll(() => refused.length, { timeout: 30_000 }).toBeGreaterThanOrEqual(1);
  expect(refused).toContain(queued.invoiceNumber);
  await expect.poll(async () => (await readOnlyQueuedRow(page)).status, { timeout: 30_000 }).toBe('Failed');
  expect(await readOnlyQueuedRow(page)).toMatchObject({ invoiceNumber: queued.invoiceNumber, sha256: queued.sha256 });

  // A reload while still barred must find the same unsent row, whether or not the app retries the upload.
  await page.goto(`${origin}/sales/new`);
  await page.locator('.shell-main').waitFor({ timeout: 30_000 });
  await triggerReconnect(page);
  await page.waitForTimeout(RETRY_SETTLE_MS);
  const afterReload = await readOnlyQueuedRow(page);
  expect(UNSYNCED).toContain(afterReload.status);
  expect(afterReload).toMatchObject({ invoiceNumber: queued.invoiceNumber, sha256: queued.sha256 });

  return { ...queued, statusAfterRefusal: 'Failed', statusAfterReload: afterReload.status, refusedAttempts: refused.length };
}
