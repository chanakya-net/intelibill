import { resolve } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { startStaticServer } from './static-server';

const account = 'a1111111-1111-4111-8111-111111111111';
const closureId = 'a2222222-2222-4222-8222-222222222222';
const adminId = 'a3333333-3333-4333-8333-333333333333';
const sessionId = 'a4444444-4444-4444-8444-444444444444';
const shopId = 'a5555555-5555-4555-8555-555555555555';
const ownerId = 'a6666666-6666-4666-8666-666666666666';
const at = '2026-11-21T04:00:00Z';
const path = `/api/admin/accounts/${account}/closures/${closureId}/shops/${shopId}/reopening`;
const route = `/restrictions/reopening?account=${account}`;

for (const width of [1440, 390]) {
  test(`reopening review, consent, and recovered delivery at ${width}px`, async ({ page }, info) => {
    const server = await startStaticServer(resolve('dist/ADMIN/browser'));
    try {
      await page.setViewportSize({ width, height: 860 });
      const facts = await install(page);
      await page.goto(server.origin + route);
      const preview = page.getByRole('button', { name: 'Review reopening' });
      await preview.click();
      await expect(page.getByText('Reopening requires active coverage other than a trial.')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Confirm reopening' })).toHaveCount(0);

      facts.enableCoverage();
      await preview.click();
      await expect(page.getByText(/Active · Paid subscription/)).toBeVisible();
      await expect(page.getByText(/Term ends 1 Dec 2026/)).toBeVisible();
      await expect(page.getByText(/Security suspension remains in force/)).toBeVisible();
      await expect(page.getByText(/No trial is created/)).toBeVisible();
      await expect(page.getByText(/The shop stays closed until the other closure/)).toBeVisible();
      const confirm = page.getByRole('button', { name: 'Confirm reopening' });
      await expect(confirm).toBeDisabled();
      await fillEvidence(page);
      await expect(confirm).toBeDisabled();
      await page.getByRole('checkbox', { name: /verified the current shop owner/ }).check();
      await expect(confirm).toBeEnabled();
      await page.screenshot({ path: info.outputPath('review.png'), fullPage: true });
      await confirm.click();
      const retry = page.getByRole('button', { name: 'Retry the same request' });
      await expect(retry).toBeVisible();
      await page.reload();
      await retry.click();
      await expect(page.getByText('Reopening decision saved.')).toBeVisible();
      await expect(page.getByTestId('reopening-propagation')).toHaveText('Pending merchant acknowledgement');
      expect(facts.requests).toHaveLength(2);
      expect(facts.requests[1]).toEqual(facts.requests[0]);
      facts.acknowledge();
      await page.getByRole('button', { name: 'Refresh delivery status' }).click();
      await expect(page.getByTestId('reopening-propagation')).toHaveText('Acknowledged by merchant service');
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      await page.screenshot({ path: info.outputPath('acknowledged.png'), fullPage: true });
    } finally {
      await server.close();
    }
  });
}

test('support reads reopening status without review controls', async ({ page }) => {
  const server = await startStaticServer(resolve('dist/ADMIN/browser'));
  try {
    const facts = await install(page, ['support']);
    await page.goto(server.origin + route);
    await expect(page.getByText('Lake Shop')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Review reopening' })).toHaveCount(0);
    facts.acknowledge();
    await page.getByRole('button', { name: 'Refresh delivery status' }).click();
    await expect(page.getByTestId('reopening-propagation')).toHaveText('Acknowledged by merchant service');
    await expect(page.getByRole('button', { name: 'Review reopening' })).toHaveCount(0);
    await expect(page.getByLabel('Owner consent evidence')).toHaveCount(0);
  } finally {
    await server.close();
  }
});

for (const malformed of [false, true]) {
  test(`blocks ${malformed ? 'malformed' : 'other-account'} reopening drafts after reload`, async ({ page }) => {
    const server = await startStaticServer(resolve('dist/ADMIN/browser'));
    try {
      const facts = await install(page);
      await page.goto(server.origin + route);
      facts.enableCoverage();
      await page.getByRole('button', { name: 'Review reopening' }).click();
      await fillEvidence(page);
      await page.getByRole('checkbox', { name: /verified the current shop owner/ }).check();
      await page.getByRole('button', { name: 'Confirm reopening' }).click();
      const retry = page.getByRole('button', { name: 'Retry the same request' });
      await expect(retry).toBeEnabled();
      await page.evaluate(async ({ admin, account, malformed }) => {
        const db = await new Promise<IDBDatabase>((resolve, reject) => {
          const open = indexedDB.open('intelibill.admin.reopenings', 1);
          open.onsuccess = () => resolve(open.result);
          open.onerror = () => reject(open.error);
        });
        await new Promise<void>((resolve, reject) => {
          const tx = db.transaction('pending', 'readwrite');
          const store = tx.objectStore('pending');
          const key = `${admin}.${account}`;
          const read = store.get(key);
          read.onsuccess = () => store.put(malformed ? { ...read.result, command: null }
            : { ...read.result, accountId: 'd0000000-0000-4000-8000-000000000000' }, key);
          tx.oncomplete = () => resolve();
          tx.onabort = () => reject(tx.error);
        });
        db.close();
      }, { admin: adminId, account, malformed });
      facts.clearPropagation();
      const unexpected: string[] = [];
      page.on('request', request => {
        if (request.method() === 'POST' && request.url().endsWith('/reopening')) unexpected.push(request.url());
      });
      await page.reload();
      await expect(page.getByText('The saved draft could not be verified.')).toBeVisible();
      await expect(retry).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Review reopening' })).toBeDisabled();
      expect(unexpected).toEqual([]);
      expect(facts.requests).toHaveLength(1);
    } finally {
      await server.close();
    }
  });
}

test('corrects pasted evidence with an embedded tab and confirms once in the same review', async ({ page, context }, info) => {
  const server = await startStaticServer(resolve('dist/ADMIN/browser'));
  try {
    await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: server.origin });
    const facts = await install(page, ['licensing-manager'], false);
    await page.goto(server.origin + route);
    facts.enableCoverage();
    await page.getByRole('button', { name: 'Review reopening' }).click();
    expect(await savedDrafts(page)).toBe(0);
    await fillEvidence(page);
    await page.getByRole('checkbox', { name: /verified the current shop owner/ }).check();
    const confirm = page.getByRole('button', { name: 'Confirm reopening' });
    await expect(confirm).toBeEnabled();
    const evidence = page.getByLabel('Owner consent evidence');
    await evidence.fill('');
    await page.evaluate(() => navigator.clipboard.writeText('Verified owner call.\tOwner asked to reopen Lake Shop.'));
    await evidence.press('ControlOrMeta+V');
    await expect(evidence).toHaveValue('Verified owner call.\tOwner asked to reopen Lake Shop.');
    const unsupported = page.getByText('Remove tabs and other control characters.');
    await expect(unsupported).toBeVisible();
    await expect(confirm).toBeDisabled();
    await expect(page.getByText('The saved draft could not be verified.')).toHaveCount(0);
    await page.screenshot({ path: info.outputPath('unsupported-evidence.png'), fullPage: true });
    expect(facts.requests).toHaveLength(0);
    expect(await savedDrafts(page)).toBe(0);

    await evidence.fill('Verified owner call. Owner asked to reopen Lake Shop.');
    await expect(unsupported).toHaveCount(0);
    await expect(confirm).toBeEnabled();
    await confirm.click();
    await expect(page.getByText('Reopening decision saved.')).toBeVisible();
    await expect(page.getByTestId('reopening-propagation')).toHaveText('Pending merchant acknowledgement');
    expect(facts.requests).toHaveLength(1);
    expect(await savedDrafts(page)).toBe(0);
  } finally {
    await server.close();
  }
});

function savedDrafts(page: Page) {
  return page.evaluate(async () => {
    const db = await new Promise<IDBDatabase>((resolve, reject) => {
      const open = indexedDB.open('intelibill.admin.reopenings', 1);
      open.onupgradeneeded = () => open.result.createObjectStore('pending');
      open.onsuccess = () => resolve(open.result);
      open.onerror = () => reject(open.error);
    });
    try {
      return await new Promise<number>((resolve, reject) => {
        const count = db.transaction('pending').objectStore('pending').count();
        count.onsuccess = () => resolve(count.result);
        count.onerror = () => reject(count.error);
      });
    } finally {
      db.close();
    }
  });
}

async function fillEvidence(page: Page) {
  await page.getByLabel('Review reason').fill('Verified current paid coverage');
  await page.getByLabel('Current shop owner user ID').fill(ownerId);
  await page.getByLabel('Owner consent date and time (IST)').fill('2026-11-21T10:00');
  await page.getByLabel('Owner consent review reference').fill('SUPPORT-898');
  await page.getByLabel('Owner consent evidence').fill('Verified owner call. Owner asked to reopen Lake Shop.');
}

async function install(page: Page, capabilities = ['licensing-manager'], loseFirstResponse = true) {
  let covered = false;
  let propagation: string | undefined;
  const requests: unknown[] = [];
  await page.route('**/api/admin/auth/session', route => route.fulfill({ json: {
    adminId, sessionId, capabilities, accessExpiresAt: new Date(Date.now() + 300_000).toISOString(),
  } }));
  await page.route('**/api/admin/auth/sign-in/context', route => route.fulfill({ json: { requestToken: 'csrf-fixture' } }));
  await page.route('**/api/admin/accounts/*/closures', route => route.fulfill({ json: [{
    id: closureId, scope: 'Shop', savedAt: at, shops: [{ shopId, shopName: 'Lake Shop',
      reopening: propagation ? { id: closureId, reopenedAt: at, propagation } : undefined }],
  }] }));
  await page.route(`**${path}/preview`, route => route.fulfill({ json: {
    closureId, shopId, shopName: 'Lake Shop', closedAt: at, version: 1, impactDigest: 'a'.repeat(64),
    coverage: covered ? { state: 'Active', basis: 'Paid', paidThrough: '2026-12-01T00:00:00Z' } : null,
    coverageAdequate: covered, otherActiveClosures: 1, survivingSuspension: { mode: 'Security', effectiveAt: at },
  } }));
  await page.route(`**${path}`, route => {
    const body = route.request().postDataJSON();
    expect(route.request().headers()).toMatchObject({ 'x-admin-session': sessionId, 'x-admin-csrf': 'csrf-fixture' });
    expect(body).toEqual({
      reason: 'Verified current paid coverage', ownerConsentConfirmed: true, ownerConsentUserId: ownerId,
      ownerConsentAt: '2026-11-21T04:30:00.000Z', ownerConsentReference: 'SUPPORT-898',
      ownerConsentEvidence: 'Verified owner call. Owner asked to reopen Lake Shop.',
      expectedVersion: 1, impactDigest: 'a'.repeat(64), operationKey: expect.stringMatching(/^[\da-f-]{36}$/),
    });
    requests.push(body);
    propagation = 'Pending';
    return loseFirstResponse && requests.length === 1 ? route.abort('connectionreset')
      : route.fulfill({ json: { id: closureId, closureId, shopId, reopenedAt: at } });
  });
  return { requests, enableCoverage: () => { covered = true; }, acknowledge: () => { propagation = 'Acknowledged'; },
    clearPropagation: () => { propagation = undefined; } };
}
