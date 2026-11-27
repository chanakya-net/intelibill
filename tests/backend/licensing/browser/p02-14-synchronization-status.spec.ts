import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';
import type { EntitlementSynchronization } from '../../../../src/frontend/shared/api/src/entitlement-synchronization';
import { origin as admin } from './p01-12-fixture';
import { account, commerce, evidence, merchant, merchantSession, prepareProvider, storedMerchantToken } from './entitlement-browser-fixture';

const sessionState = (role: 'owner' | 'staff') => join(process.env['P0214_SESSION_DIR']!, role + '.json');

const states = {
  saved: ['Pending', 'Queued', 'Saved, waiting for confirmation', 'Queued'],
  unacknowledged: ['Pending', 'AwaitingAcknowledgement', 'Saved, waiting for confirmation', 'Awaiting acknowledgement'],
  applied: ['Applied', 'Acknowledged', 'Applied', 'Acknowledged'],
  'needs-review': ['NeedsReview', 'DeadLetter', 'Needs review', 'Delivery attempts exhausted'],
  converged: ['Applied', 'Acknowledged', 'Applied', 'Acknowledged'],
} as const;

async function merchantStatus(page: Page) {
  const read = page.waitForResponse(response => /^\/api\/licensing\/shops\/[0-9a-f-]{36}\/status$/.test(
    new URL(response.url()).pathname));
  await page.goto(merchant + '/billing/status');
  const response = await read;
  expect(response.headers()['cache-control']).toContain('no-store');
  await expect(page.getByTestId('observe-only')).toContainText('does not block');
  return response;
}

async function readProvider(browser: Browser, phase: keyof typeof states, shopId: string, expected: EntitlementSynchronization) {
  const page = await browser.newPage({
    viewport: { width: 1280, height: 900 }, storageState: process.env['P0213_PROVIDER_STATE']!,
  });
  try {
    await page.goto(admin + '/accounts');
    await page.getByLabel('Billing account ID').fill(account);
    const read = page.waitForResponse(response => new URL(response.url()).pathname === `/api/admin/accounts/${account}/status`);
    await page.getByRole('button', { name: 'Read access status' }).click();
    const response = await read;
    expect(response.status()).toBe(200);
    expect(response.headers()['cache-control']).toContain('no-store');
    const body = await response.json();
    expect(body.observeOnly).toBe(true);
    expect(body.entitlementSynchronization).toBe(expected.state);
    const shop = body.items.flatMap((item: { coveredShops: Array<{ shopId: string; synchronization: EntitlementSynchronization }> }) =>
      item.coveredShops).find((covered: { shopId: string }) => covered.shopId === shopId);
    expect(shop?.synchronization).toEqual(expected);
    await expect(page.getByTestId('provider-observe-only')).toContainText('does not block');
    await expect(page.getByTestId('provider-trial-ends')).toHaveText('24 Feb 2027, 05:30:00.123456 IST');
    await assertPanel(page, expected, states[phase], 'provider-');
    await page.screenshot({ path: join(evidence, `provider-${phase}-desktop.png`), fullPage: true });
    if (phase === 'needs-review') {
      await page.setViewportSize({ width: 390, height: 844 });
      await page.screenshot({ path: join(evidence, `provider-${phase}-mobile.png`), fullPage: true });
    }
  } finally {
    await page.close();
  }
}

async function assertPanel(page: Page, sync: EntitlementSynchronization,
  [state, delivery, label, deliveryLabel]: (typeof states)[keyof typeof states], prefix = '') {
  expect(sync.state).toBe(state);
  expect(sync.delivery).toBe(delivery);
  await expect(page.getByTestId(prefix + 'sync-state')).toHaveText(label);
  await expect(page.getByTestId(prefix + 'sync-delivery')).toHaveText(deliveryLabel);
  await expect(page.getByTestId(prefix + 'sync-saved')).toHaveText(`Account ${sync.savedAccountRevision} · Shop ${sync.savedShopRevision}`);
  await expect(page.getByTestId(prefix + 'sync-applied')).toHaveText(`Account ${sync.appliedAccountRevision} · Shop ${sync.appliedShopRevision}`);
  await expect(page.getByTestId(prefix + 'sync-time')).toHaveText(ist(sync.lastSynchronizedAt!));
  if (state === 'NeedsReview') await expect(page.getByTestId(prefix + 'sync-attention')).toHaveText('Unavailable');
}

function ist(value: string) {
  const parts = new Intl.DateTimeFormat('en-IN', { timeZone: 'Asia/Kolkata', day: 'numeric', month: 'short',
    year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
  }).formatToParts(new Date(value));
  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find(item => item.type === type)?.value;
  const fraction = value.match(/T\d{2}:\d{2}:\d{2}(\.\d+)/)?.[1] ?? '';
  return `${part('day')} ${part('month')} ${part('year')}, ${part('hour')}:${part('minute')}:${part('second')}${fraction} IST`;
}

async function probePrivateRoutes(page: Page) {
  const statuses: number[] = [];
  for (const web of [merchant, admin]) {
    for (const action of ['entitlements', 'merchant-events/acknowledgement', 'merchant-events/reconciliation']) {
      const probe = await page.request.post(`${web}/internal/licensing/v1/${action}`, { data: {} });
      if (probe.status() === 200) {
        expect(probe.headers()['content-type']).toContain('text/html');
      } else {
        expect([404, 405]).toContain(probe.status());
      }
      statuses.push(probe.status());
    }
  }
  return statuses;
}

test('@prepare signs in the local operator and merchants and publishes the trial policy', async ({ page, browser }) => {
  await prepareProvider(page);
  for (const role of ['owner', 'staff'] as const) {
    const merchantPage = await browser.newPage();
    try {
      await merchantSession(merchantPage, role);
      await merchantPage.context().storageState({ path: sessionState(role) });
    } finally {
      await merchantPage.close();
    }
  }
});

for (const phase of Object.keys(states) as Array<keyof typeof states>) {
  test(`@${phase} both shipping screens report the same synchronization facts`, async ({ browser }) => {
    const staff = await browser.newPage({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true,
      timezoneId: 'America/Los_Angeles', storageState: sessionState('staff') });
    try {
      const response = await merchantStatus(staff);
      const token = await storedMerchantToken(staff);
      expect(response.status()).toBe(200);
      const body = await response.json();
      expect(body.accountId).toBeNull();
      expect(body.term.commercial).toBeNull();
      expect(body.term.basis).toBe('Trial');
      expect(body.state).toBe('Active');
      await expect(staff.getByTestId('paid-through')).toHaveText('24 Feb 2027, 05:30:00.123456 IST');
      await expect(staff.getByTestId('commercial-price')).toHaveCount(0);
      const sync: EntitlementSynchronization = body.synchronization;
      await assertPanel(staff, sync, states[phase]);
      await staff.screenshot({ path: join(evidence, `merchant-${phase}-mobile.png`), fullPage: true });
      await readProvider(browser, phase, body.shopId, sync);
      const stock = await commerce(browser, staff, token, phase, sessionState('owner'));
      const privateStatus = await probePrivateRoutes(staff);
      writeFileSync(join(evidence, `observations-${phase}.json`), JSON.stringify({
        synchronization: sync, staffPriceVisible: false, stock, privateStatus,
      }, null, 2));
    } finally {
      await staff.close();
    }
  });
}

test('@outage clears status and retains observe-only commerce', async ({ browser }) => {
  const staff = await browser.newPage({ viewport: { width: 390, height: 844 }, storageState: sessionState('staff') });
  try {
    expect((await merchantStatus(staff)).status()).toBe(503);
    const token = await storedMerchantToken(staff);
    await expect(staff.getByTestId('verification-unavailable')).toContainText('not an expiry');
    await expect(staff.getByTestId('entitlement-sync')).toHaveCount(0);
    const stock = await commerce(browser, staff, token, 'outage', sessionState('owner'));
    await staff.screenshot({ path: join(evidence, 'merchant-outage-mobile.png'), fullPage: true });
    writeFileSync(join(evidence, 'observations-outage.json'), JSON.stringify({ http: 503, syncVisible: false, stock }));
  } finally {
    await staff.close();
  }
});
