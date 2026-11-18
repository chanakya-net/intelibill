import { expect, test, type Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { startStaticServer } from './static-server';

const accountId = 'a1111111-1111-4111-8111-111111111111';
const shopId = 'b1111111-1111-4111-8111-111111111111';
const otherShopId = 'c1111111-1111-4111-8111-111111111111';
const adminId = 'a2222222-2222-4222-8222-222222222222';
const sessionId = 'd1111111-1111-4111-8111-111111111111';
const customerExplanation = 'Contact support about access.';
const internalReason = 'Private simulated review evidence.';
const shops = [
  { shopId, shopName: 'Indiranagar Shop', coverageMode: 'Independent' },
  { shopId: otherShopId, shopName: 'Jayanagar Shop', coverageMode: 'Independent' },
];

for (const viewport of [{ width: 1440, height: 900 }, { width: 390, height: 844 }]) {
  test(`suspension impact and acknowledgement at ${viewport.width}px`, async ({ page }, testInfo) => {
    const server = await startStaticServer(resolve('dist/ADMIN/browser'));
    const status = {
      id: 'e1111111-1111-4111-8111-111111111111', accountId, shopId: null, version: 1,
      recordedAt: '2027-02-10T00:00:00.123456Z', effectiveAt: '2027-02-10T00:00:00.123456Z',
      customerExplanation, mode: 'Security',
    };
    const state = (propagation: string) => ({ ...status, propagation, shops: shops.map((shop, index) => ({
      ...shop, accountRevision: 8, shopRevision: 10 + index, status: propagation,
    })) });
    let submitted = 0;
    let acknowledged = false;
    try {
      await page.setViewportSize(viewport);
      await page.route('**/api/admin/auth/session', route => route.fulfill({ json: {
        adminId, sessionId,
        capabilities: ['security-administrator'], accessExpiresAt: new Date(Date.now() + 300_000).toISOString(),
      } }));
      await page.route('**/api/admin/auth/sign-in/context', route => route.fulfill({ json: { requestToken: 'fixture-csrf' } }));
      await page.route(url => url.pathname.startsWith('/api/admin/accounts/') && url.pathname.includes('/restrictions'), async route => {
        const request = route.request();
        expect(request.headers()['x-admin-session']).toBe(sessionId);
        if (request.method() === 'GET') {
          await route.fulfill({ json: submitted ? [state(acknowledged ? 'Acknowledged' : 'Pending')] : [] });
          return;
        }
        const body = request.postDataJSON();
        expect(request.headers()['x-admin-csrf']).toBe('fixture-csrf');
        if (new URL(request.url()).pathname.endsWith('/preview')) {
          expect(body.shopId).toBe(body.scope === 'Shop' ? shopId : null);
          await route.fulfill({ json: preview(body.scope) });
          return;
        }
        submitted++;
        expect(body).toMatchObject({ scope: 'Account', shopId: null, mode: 'Security', expectedVersion: 0,
          effectiveAt: null, customerExplanation, internalReason, reviewReference: 'CASE-878', impactDigest: preview('Account').impactDigest });
        expect(body).not.toHaveProperty('isValid');
        await route.fulfill({ status: 201, json: state('Pending') });
      });

      await page.goto(server.origin + '/');
      await page.getByRole('link', { name: 'Operational restrictions' }).click();
      await page.getByLabel('Billing account ID').fill(accountId);
      await page.getByRole('button', { name: 'Preview impact' }).click();
      await expect(page.getByRole('alert')).toContainText('Select account scope');
      await page.getByLabel('Suspension scope').selectOption('Shop');
      await page.getByLabel('Shop ID', { exact: true }).fill(shopId);
      await page.getByRole('button', { name: 'Preview impact' }).click();
      await expect(page.getByText('Indiranagar Shop (Independent)')).toBeVisible();
      await expect(page.getByText('Jayanagar Shop (Independent)')).toHaveCount(0);
      await page.getByLabel('Suspension scope').selectOption('Account');
      await expect(page.getByRole('heading', { name: 'Affected shops' })).toHaveCount(0);
      await page.getByRole('button', { name: 'Preview impact' }).click();
      await expect(page.getByText('Jayanagar Shop (Independent)')).toBeVisible();
      await page.getByLabel('Suspension mode').selectOption('Security');
      await page.getByLabel('Customer explanation').fill(customerExplanation);
      await page.getByRole('button', { name: 'Apply suspension' }).click();
      await expect(page.getByRole('alert')).toContainText('Complete the mode');
      expect(submitted).toBe(0);
      await page.getByLabel('Internal review reason').fill(internalReason);
      await page.getByLabel('Review reference').fill('CASE-878');
      await page.screenshot({ path: testInfo.outputPath('review.png'), fullPage: true });

      await page.getByRole('button', { name: 'Apply suspension' }).click();
      await expect(page.getByRole('status')).toHaveText('Pending merchant acknowledgement');
      await expect(page.getByText(/Account revision 8, shop revision 11/)).toBeVisible();
      await expect(page.getByText(internalReason)).toHaveCount(0);
      await page.screenshot({ path: testInfo.outputPath('pending.png'), fullPage: true });
      acknowledged = true;
      await page.getByRole('button', { name: 'Refresh saved restrictions' }).click();
      await expect(page.getByRole('status')).toHaveText('Acknowledged by merchant service');
      await expect(page.getByText(/Account revision 8, shop revision 11/)).toBeVisible();
      expect(submitted).toBe(1);
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(viewport.width);
      await page.screenshot({ path: testInfo.outputPath('acknowledged.png'), fullPage: true });
    } finally {
      await server.close();
    }
  });
}

function preview(scope: 'Account' | 'Shop') {
  const selection = { scope, shopId: scope === 'Shop' ? shopId : null };
  const evidence = process.env['P0403_PREVIEW_EVIDENCE_DIR'];
  const actual = evidence
    ? JSON.parse(readFileSync(resolve(evidence, `preview-${scope}.json`), 'utf8'))
    : {
      accountId, accountName: 'Bengaluru Retail', selection, version: 0, alreadySuspended: false,
      impactDigest: '8'.repeat(64), shops: scope === 'Shop' ? shops.slice(0, 1) : shops,
    };
  // Keep the real preview shape, including any unexpected serialized selection properties.
  return { ...actual, accountId, selection: { ...actual.selection, shopId: selection.shopId },
    shops: actual.shops.map((shop: object, index: number) => ({ ...shop, ...shops[index] })) };
}

for (const scope of ['Account', 'Shop'] as const) {
  test(`recovers the saved ${scope} draft after reload, a lost response and failed CSRF context`, async ({ page }) => {
    const server = await startStaticServer(resolve('dist/ADMIN/browser'));
    const alternateAdmin = 'b2222222-2222-4222-8222-222222222222';
    const command = {
      scope, shopId: scope === 'Shop' ? shopId : null, operationKey: 'e1111111-1111-4111-8111-111111111111',
      mode: 'Trading', effectiveAt: '2027-02-10T00:00:00Z', customerExplanation,
      internalReason, reviewReference: 'CASE-878', expectedVersion: 0, impactDigest: preview(scope).impactDigest,
    };
    let actor = adminId;
    let contextStatus = 200;
    let submitted = 0;
    let versionConflict = false;
    try {
      await page.route('**/api/admin/auth/session', route => route.fulfill({ json: {
        adminId: actor, sessionId, capabilities: ['security-administrator'],
        accessExpiresAt: new Date(Date.now() + 300_000).toISOString(),
      } }));
      await page.route('**/api/admin/auth/sign-in/context', route => contextStatus === 200
        ? route.fulfill({ json: { requestToken: 'fixture-csrf' } })
        : route.fulfill({ status: contextStatus, json: { code: 'Suspension.VersionConflict' } }));
      await page.route('**/api/admin/accounts/*/restrictions**', async route => {
        const request = route.request();
        if (request.method() === 'GET') return route.fulfill({ json: [] });
        if (request.url().endsWith('/preview')) return route.fulfill({ json: preview(request.postDataJSON().scope) });
        submitted++;
        expect(request.postDataJSON()).toEqual(command);
        if (versionConflict) return route.fulfill({ status: 409, json: { code: 'Suspension.VersionConflict' } });
        if (submitted === 1) return route.abort('failed');
        if (submitted === 2) return route.fulfill({ status: 404, body: 'Proxy unavailable' });
        return route.fulfill({ json: {
          id: command.operationKey, accountId, shopId: command.shopId, propagation: 'Pending',
          mode: command.mode, customerExplanation, effectiveAt: command.effectiveAt,
          recordedAt: command.effectiveAt, shops: [],
        } });
      });

      const reviewStored = async () => {
        await page.goto(server.origin + '/restrictions');
        await page.getByLabel('Billing account ID').fill(accountId);
        await page.getByLabel('Suspension scope').selectOption(scope === 'Account' ? 'Shop' : 'Account');
        if (scope === 'Account') await page.getByLabel('Shop ID', { exact: true }).fill(shopId);
        await page.getByRole('button', { name: 'Preview impact' }).click();
      };
      const retry = page.getByRole('button', { name: 'Retry saved request' });
      const pending = page.getByTestId('pending-suspension');
      const frozenTarget = scope === 'Account' ? `Entire billing account ${accountId}` : `Shop ${shopId}`;
      await page.goto(server.origin + '/');
      await seedDraft(page, { adminId, accountId, command: { ...command, isValid: true } });
      await reviewStored();
      await expect(pending).toContainText(frozenTarget);
      await expect(pending).toContainText('Trading suspension');
      await expect(pending).toContainText('05:30:00 IST');
      expect(submitted).toBe(0);

      actor = alternateAdmin;
      await reviewStored();
      await expect(page.getByRole('heading', { name: 'Affected shops' })).toBeVisible();
      await expect(retry).toHaveCount(0);
      actor = adminId;
      await reviewStored();
      await retry.click();
      await expect(page.getByRole('alert')).toContainText('unconfirmed');
      expect(submitted).toBe(1);

      contextStatus = 409;
      await retry.click();
      await expect(page.getByRole('alert')).toContainText('unconfirmed');
      await expect(retry).toBeEnabled();
      await expect(pending).toContainText(frozenTarget);
      expect(submitted).toBe(1);

      contextStatus = 200;
      await reviewStored();
      await retry.click();
      await expect(page.getByRole('alert')).toContainText('unconfirmed');
      await expect(pending).toBeVisible();
      await reviewStored();
      await retry.click();
      await expect(page.getByRole('heading', { name: scope === 'Account' ? 'Trading · Entire account' : 'Trading · One shop' })).toBeVisible();
      await expect(retry).toHaveCount(0);
      expect(submitted).toBe(3);

      versionConflict = true;
      await seedDraft(page, { adminId, accountId, command });
      await reviewStored();
      await retry.click();
      await expect(page.getByRole('alert')).toContainText('Preview it again');
      await expect(retry).toHaveCount(0);
      await reviewStored();
      await expect(page.getByRole('heading', { name: 'Affected shops' })).toBeVisible();
      await expect(retry).toHaveCount(0);
    } finally {
      await server.close();
    }
  });
}

async function seedDraft(page: Page, draft: { adminId: string; accountId: string; command: object }) {
  await page.evaluate(draft => new Promise<void>((resolve, reject) => {
    const request = indexedDB.open('intelibill.admin.suspensions', 1);
    request.onupgradeneeded = () => request.result.createObjectStore('pending');
    request.onerror = () => reject(request.error);
    request.onsuccess = () => {
      const db = request.result;
      const tx = db.transaction('pending', 'readwrite');
      tx.objectStore('pending').put(draft, `${draft.adminId}.${draft.accountId}`);
      tx.oncomplete = () => { db.close(); resolve(); };
      tx.onabort = () => { db.close(); reject(tx.error); };
    };
  }), draft);
}

for (const width of [1440, 390]) {
  test(`P04-04 previews and recovers an explicit release at ${width}px`, async ({ page }, info) => {
    const server = await startStaticServer(resolve('dist/ADMIN/browser'));
    const id = 'e1111111-1111-4111-8111-111111111111';
    const recordedAt = '2027-03-07T04:30:00Z';
    const base = { id, accountId, shopId: null, mode: 'Security', version: 1, recordedAt,
      effectiveAt: recordedAt, customerExplanation, propagation: 'Acknowledged', shops: [] };
    const released = (propagation: string) => ({ ...base, release: { releasedAt: recordedAt, releasedBy: adminId, propagation,
      shops: shops.map(shop => ({ ...shop, accountRevision: 9, shopRevision: 11, status: propagation })) } });
    const writes: object[] = [];
    let acknowledged = false;
    try {
      await page.setViewportSize({ width, height: 860 });
      await page.route('**/api/admin/auth/session', route => route.fulfill({ json: {
        adminId, sessionId, capabilities: ['security-administrator'],
        accessExpiresAt: new Date(Date.now() + 300_000).toISOString(),
      } }));
      await page.route('**/api/admin/auth/sign-in/context', route => route.fulfill({ json: { requestToken: 'fixture-csrf' } }));
      await page.route(`**/api/admin/accounts/${accountId}/restrictions**`, async route => {
        const request = route.request();
        expect(request.headers()['x-admin-session']).toBe(sessionId);
        if (request.method() === 'GET') return route.fulfill({ json: writes.length
          ? [released(acknowledged ? 'Acknowledged' : 'Pending')] : [base] });
        expect(request.headers()['x-admin-csrf']).toBe('fixture-csrf');
        if (request.url().endsWith(`/${id}/release/preview`)) return route.fulfill({ json: {
          restrictionId: id, version: 1, evaluatedAt: recordedAt, impactDigest: '8'.repeat(64),
          shops: [{ ...shops[0], state: 'Expired', basis: 'Paid', paidThrough: '2027-02-28T04:30:00Z',
            graceEndsAt: recordedAt, accessEndsAt: recordedAt, survivingSuspension: null },
          { ...shops[1], state: 'Active', basis: 'Paid', survivingSuspension: { mode: 'Trading', effectiveAt: recordedAt } }],
        } });
        expect(request.url()).toMatch(new RegExp(`/${id}/release$`));
        writes.push(request.postDataJSON());
        if (writes.length === 1) return route.abort('failed');
        return route.fulfill({ json: released('Pending') });
      });
      const read = async () => {
        await page.goto(server.origin + '/restrictions');
        await page.getByLabel('Billing account ID').fill(accountId);
        await page.getByRole('button', { name: 'Refresh saved restrictions' }).click();
      };
      await read();
      await page.getByRole('button', { name: 'Preview release' }).click();
      await expect(page.getByRole('heading', { name: 'Access after release' })).toBeVisible();
      await expect(page.getByText(/Trading suspension remains/)).toBeVisible();
      await expect(page.getByText(/Expired/)).toBeVisible();
      await expect(page.getByRole('button', { name: 'Confirm release' })).toBeDisabled();
      expect(writes).toHaveLength(0);
      await page.getByLabel('Release reason').fill('Security review completed.');
      await page.screenshot({ path: info.outputPath('release-preview.png'), fullPage: true });
      await page.getByRole('button', { name: 'Confirm release' }).click();
      await expect(page.getByRole('alert')).toContainText('unconfirmed');
      await read();
      await expect(page.getByTestId('pending-suspension')).toContainText(id);
      await page.getByRole('button', { name: 'Retry saved request' }).click();
      await expect(page.getByTestId('pending-suspension')).toHaveCount(0);
      await expect(page.getByTestId('release-propagation')).toContainText('Pending merchant acknowledgement');
      await page.screenshot({ path: info.outputPath('release-pending.png'), fullPage: true });
      acknowledged = true;
      await page.getByRole('button', { name: 'Refresh saved restrictions' }).click();
      await expect(page.getByTestId('release-propagation')).toContainText('Acknowledged by merchant service');
      await expect(page.getByRole('heading', { name: 'Security · Entire account' })).toBeVisible();
      expect(writes).toHaveLength(2);
      expect(writes[0]).toMatchObject({ reason: 'Security review completed.', expectedVersion: 1, impactDigest: '8'.repeat(64) });
      expect(writes[1]).toEqual(writes[0]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      await page.screenshot({ path: info.outputPath('release-acknowledged.png'), fullPage: true });
    } finally {
      await server.close();
    }
  });
}
