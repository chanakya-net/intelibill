import { existsSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

import { expect, test } from '@playwright/test';

import { BUILD, sha256Hex } from './legacy-queue-recovery-fixture';
import {
  MSG_EXPIRED,
  addBatchToCart,
  enableWithFixture,
  goOfflineFromWorker,
  queuedDialog,
  submit,
  triggerReconnect,
} from './licensing-offline-fixtures';
import { runLiveMissedSuspensionScenario, runLiveOrdinaryExpiryScenario } from './licensing-offline-live';
import {
  expectClockRefusal,
  expectQueuePreservedWhenUploadBarred,
  forceVerifiedRefresh,
  routeAcceptedUploads,
  routeStaleAuthority,
} from './licensing-offline-scenarios';
import {
  LEASE_ID,
  PINNED_BUILD,
  buildPinnedApp,
  canonicalInstant,
  createFixtureSigner,
  durableState,
  readClockRow,
  readLeaseRow,
  readQueueRows,
  type FixtureSigner,
} from './offline-permit-finalization-fixture';
import { renewalRoutes, renewedLease } from './offline-permit-renewal-routes';
import { startPurchaseReceivingServer } from './purchase-receiving-server';

const liveScenario = process.env['P0509_LIVE_SCENARIO'];
test.describe.configure({ timeout: 240_000 });

if (liveScenario === 'ordinary-expiry') {
  test('P05-09 live browser sale recovers after ordinary expiry', async ({ page, context }) => {
    await runLiveOrdinaryExpiryScenario(page, context);
  });
} else if (liveScenario === 'missed-suspension') {
  test('P05-09 live browser missed suspension retains one review', async ({ page, context }) => {
    await runLiveMissedSuspensionScenario(page, context);
  });
} else {
  let signer: FixtureSigner;
  let server: Awaited<ReturnType<typeof startPurchaseReceivingServer>> | undefined;
  const evidenceLog: Record<string, unknown> = {};

  test.beforeAll(() => {
    test.setTimeout(300_000);
    signer = createFixtureSigner('p0509-release-key-1');
    buildPinnedApp({ [signer.keyId]: signer.publicKeySpki });
    for (const file of ['ngsw-worker.js', 'ngsw-angular-worker.js', 'ngsw.json', 'ngsw-lite.json']) {
      if (!existsSync(join(BUILD, file))) {
        throw new Error(`No ${file} in ${BUILD}. Run \`bun run build\` in src/frontend first.`);
      }
    }
  });

  test.afterAll(() => {
    const dir = process.env['P0509_EVIDENCE_DIR'];
    if (dir && existsSync(dir)) {
      writeFileSync(join(dir, 'browser-fixture-evidence.json'), JSON.stringify(evidenceLog, null, 2));
    }
  });

  test.afterEach(async () => {
    await server?.close();
    server = undefined;
  });

  test('exact millisecond deadline allows validUntil - 1 ms and refuses exact validUntil in independent contexts', async ({ browser }) => {
    server = await startPurchaseReceivingServer(PINNED_BUILD);
    const fixed = Math.floor(Date.now() / 1_000) * 1_000;
    const endMs = fixed + 10 * 60_000;
    const validUntil = canonicalInstant(endMs);

    // Context 1: pin exact validUntil - 1 ms during submission -> issues A/2627/000001.
    const ctxAllowed = await browser.newContext({ locale: 'en-IN', timezoneId: 'Asia/Kolkata', serviceWorkers: 'allow' });
    let allowedDigest = '';
    try {
      const page = await ctxAllowed.newPage();
      await page.clock.install({ time: fixed });
      await enableWithFixture(page, signer, server.origin, { now: () => fixed, fields: { validUntil } });
      await ctxAllowed.unrouteAll({ behavior: 'ignoreErrors' });
      await goOfflineFromWorker(page, ctxAllowed, () => server!.assets.requests.length);
      await addBatchToCart(page);
      await page.clock.setFixedTime(endMs - 1);
      await submit(page).click();
      await expect(queuedDialog(page)).toContainText('A/2627/000001');
      const [row] = await readQueueRows(page);
      allowedDigest = sha256Hex(JSON.stringify(row.payload));
      expect((await readLeaseRow(page))!.lease).toMatchObject({ nextNumber: 2, remainingCount: 99 });
      expect((await readLeaseRow(page))!.claim ?? null).toBeNull();
    } finally {
      await ctxAllowed.close();
    }

    // Context 2: independent context pins exact validUntil -> refuses with zero durable side effects.
    const ctxRefused = await browser.newContext({ locale: 'en-IN', timezoneId: 'Asia/Kolkata', serviceWorkers: 'allow' });
    try {
      const page = await ctxRefused.newPage();
      await page.clock.install({ time: fixed });
      await enableWithFixture(page, signer, server.origin, { now: () => fixed, fields: { validUntil } });
      await ctxRefused.unrouteAll({ behavior: 'ignoreErrors' });
      await goOfflineFromWorker(page, ctxRefused, () => server!.assets.requests.length);
      await addBatchToCart(page);
      const before = await durableState(page);
      expect(before).toMatchObject({ nextNumber: 1, remainingCount: 100, claim: null, queueCount: 0 });
      await page.clock.setFixedTime(endMs);
      await submit(page).click();
      await expect(page.getByText(MSG_EXPIRED, { exact: false }).first()).toBeVisible();
      await expect(page.getByTestId('offline-permit-ended')).toContainText('IST');
      await expect(queuedDialog(page)).toHaveCount(0);
      expect(await durableState(page)).toEqual(before);
    } finally {
      await ctxRefused.close();
    }

    evidenceLog['exactBoundary'] = {
      validUntil, allowedMs: endMs - 1, refusedMs: endMs, allowedPayloadSha256: allowedDigest, refusedDurableUnchanged: true,
    };
  });

  test('clock rollback beyond 5s tolerance latches across restore and offline reload until later signed verification', async ({ page, context }) => {
    server = await startPurchaseReceivingServer(PINNED_BUILD);
    const fixed = Math.floor(Date.now() / 1_000) * 1_000;
    let serverNow = fixed;
    await page.clock.install({ time: fixed });
    const fixture = await enableWithFixture(page, signer, server.origin, { now: () => serverNow });
    await goOfflineFromWorker(page, context, () => server!.assets.requests.length);

    await page.clock.setSystemTime(fixed + 120_000);
    await addBatchToCart(page);
    await submit(page).click();
    await expect(queuedDialog(page)).toContainText('A/2627/000001');
    const [firstRow] = await readQueueRows(page);
    const firstSha = sha256Hex(JSON.stringify(firstRow.payload));

    await queuedDialog(page).getByRole('button').last().click();
    await page.goto(`${server.origin}/sales/new`);
    await page.locator('.shell-main').waitFor({ timeout: 30_000 });
    await addBatchToCart(page);

    // Roll back 6 seconds (> 5000 ms tolerance) inside the still-valid 10-minute window.
    await page.clock.setSystemTime(fixed + 114_000);
    const before = await durableState(page);
    await expectClockRefusal(page, before);
    expect((await readClockRow(page))!.checkpoint['blocked']).toMatchObject({ reason: 'WallClockMovedBack' });

    // Restoring wall time and reloading offline keep the refusal latched.
    await page.clock.setSystemTime(fixed + 180_000);
    await expectClockRefusal(page, before);

    await page.reload();
    await page.locator('.shell-main').waitFor({ timeout: 30_000 });
    expect((await readClockRow(page))!.checkpoint['blocked']).toMatchObject({ reason: 'WallClockMovedBack' });
    await addBatchToCart(page);
    await expectClockRefusal(page, before);

    // Strictly later signed observation clears the latch and allows the next sale.
    serverNow = fixed + 190_000;
    await page.clock.setSystemTime(serverNow);
    await context.setOffline(false);
    await forceVerifiedRefresh(page, fixture);

    const refreshed = fixture.delivered.at(-1)!;
    await expect.poll(async () => (await readClockRow(page))?.checkpoint['permitId'], { timeout: 30_000 }).toBe(refreshed.fields.permitId);
    expect((await readClockRow(page))!.checkpoint['blocked']).toBeNull();

    await context.setOffline(true);
    await submit(page).click();
    await expect(queuedDialog(page)).toContainText('A/2627/000002');
    const rowsAfter = await readQueueRows(page);
    expect(sha256Hex(JSON.stringify(rowsAfter.find((r) => r.invoiceNumber === 'A/2627/000001')!.payload))).toBe(firstSha);
    expect(rowsAfter.find((r) => r.invoiceNumber === 'A/2627/000002')!.payload['licenceEvidence']['permitId']).toBe(refreshed.fields.permitId);

    evidenceLog['clockRollback'] = {
      rollbackDeltaMs: -6_000, latchReason: 'WallClockMovedBack', firstPayloadSha256: firstSha,
      clearedByPermitId: refreshed.fields.permitId,
    };
  });

  test('unseen renewal and stale previews keep held deadline; verified reconnect renews only future invoices', async ({ page, context }) => {
    server = await startPurchaseReceivingServer(PINNED_BUILD);
    await page.clock.install();
    const fixture = await enableWithFixture(page, signer, server.origin);
    const original = fixture.delivered.at(-1)!;

    await goOfflineFromWorker(page, context, () => server!.assets.requests.length);
    await addBatchToCart(page);
    await submit(page).click();
    await expect(queuedDialog(page)).toContainText('A/2627/000001');
    const [firstQueued] = await readQueueRows(page);
    const firstSha = sha256Hex(JSON.stringify(firstQueued.payload));

    const verified = Date.parse(original.fields.validUntil) + 1_000;
    const counts = await renewalRoutes(page, signer, verified);
    await page.clock.setSystemTime(verified + 1_000);
    await page.reload();
    await page.locator('.shell-main').waitFor({ timeout: 30_000 });
    await addBatchToCart(page);
    await submit(page).click();
    await expect(page.getByTestId('offline-permit-ended')).toBeVisible();
    expect((await readLeaseRow(page))?.lease.leaseId).toBe(LEASE_ID);
    expect(counts.reservations).toBe(0);

    const stale = await routeStaleAuthority(page, original.fields.authorityVerifiedAt);
    const uploadedPayloads = await routeAcceptedUploads(page);

    await context.setOffline(false);
    await triggerReconnect(page);
    await expect.poll(() => stale.state.polls, { timeout: 20_000 }).toBeGreaterThanOrEqual(1);
    await expect.poll(() => uploadedPayloads.length, { timeout: 20_000 }).toBeGreaterThanOrEqual(1);
    expect(counts.reservations).toBe(0);
    expect((await readLeaseRow(page))?.lease.leaseId).toBe(LEASE_ID);
    const wireFirst = uploadedPayloads[0];
    expect((wireFirst['licenceEvidence'] as Record<string, unknown>)['signedPermit']).toBe(original.token);

    // Unroute only the stale override so renewalRoutes' fresh authority handler answers next reconnect.
    await stale.remove();
    await triggerReconnect(page);
    await expect.poll(async () => (await readLeaseRow(page))?.lease.leaseId, { timeout: 25_000 }).toBe(renewedLease);
    expect(counts.reservations).toBe(1);

    const [preserved] = await readQueueRows(page);
    expect(sha256Hex(JSON.stringify(preserved.payload))).toBe(firstSha);

    await context.setOffline(true);
    await submit(page).click();
    await expect(queuedDialog(page)).toContainText('A/2627/000101');
    const second = (await readQueueRows(page)).find((r) => r.invoiceNumber === 'A/2627/000101')!;
    expect(second.payload['licenceEvidence']['leaseId']).toBe(renewedLease);
    expect(second.payload['licenceEvidence']['signedPermit']).not.toBe(original.token);

    evidenceLog['renewal'] = {
      originalPermitId: original.fields.permitId, renewedLeaseId: renewedLease,
      renewedPermitId: second.payload['licenceEvidence']['permitId'],
      firstPayloadSha256: firstSha, firstWireSha256: sha256Hex(JSON.stringify(wireFirst)),
      stalePolls: stale.state.polls, reservations: counts.reservations,
    };
  });

  test('security-barred upload keeps the queued invoice on the device, unsent and byte-identical', async ({ page, context }) => {
    server = await startPurchaseReceivingServer(PINNED_BUILD);
    await enableWithFixture(page, signer, server.origin);
    await goOfflineFromWorker(page, context, () => server!.assets.requests.length);
    await addBatchToCart(page);
    await submit(page).click();
    await expect(queuedDialog(page)).toContainText('A/2627/000001');
    await queuedDialog(page).getByRole('button').last().click();

    evidenceLog['securityBarredUpload'] = await expectQueuePreservedWhenUploadBarred(page, server.origin);
  });
}
