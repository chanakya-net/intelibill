import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Page } from '@playwright/test';
import { origin as admin } from './p01-12-fixture';
import { account, commerce, evidence, merchant, merchantSession, prepareProvider } from './entitlement-browser-fixture';

const trialEnd = '24 Feb 2027, 05:30:00.123456 IST';


async function merchantStatus(page: Page, phase: string) {
  const pending = page.waitForResponse(response => /^\/api\/licensing\/shops\/[0-9a-f-]{36}\/status$/.test(
    new URL(response.url()).pathname));
  await page.goto(merchant + '/billing/status');
  const response = await pending;
  expect(response.headers()['cache-control']).toContain('no-store');
  await expect(page.getByTestId('observe-only')).toContainText('does not block');
  if (phase === 'outage') {
    expect(response.status()).toBe(503);
    await expect(page.getByTestId('verification-unavailable')).toContainText('not an expiry');
    await expect(page.getByTestId('status-state')).toHaveCount(0);
    return { http: 503, view: 'Unavailable' };
  }
  expect(response.status()).toBe(200);
  const body = await response.json();
  expect(body.accountId).toBeNull();
  expect(body.term.commercial).toBeNull();
  expect(body.entitlementSynchronization).toBe(phase === 'recovered' ? 'Applied' : 'Pending');
  expect(body.term.basis).toBe('Trial');
  expect(body.state).toBe(phase === 'expired' ? 'Expired' : 'Active');
  await expect(page.getByTestId('paid-through')).toHaveText(trialEnd);
  await expect(page.getByTestId('commercial-price')).toHaveCount(0);
  return { http: 200, view: body.state, basis: body.term.basis, deadline: body.term.accessEndsAt, priceVisible: false };
}

async function providerStatus(browser: Browser) {
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 },
    storageState: process.env['P0213_PROVIDER_STATE']! });
  try {
    await page.goto(admin + '/accounts');
    await page.getByLabel('Billing account ID').fill(account);
    const read = page.waitForResponse(response => new URL(response.url()).pathname === `/api/admin/accounts/${account}/status`);
    await page.getByRole('button', { name: 'Read access status' }).click();
    expect((await read).status()).toBe(200);
    await expect(page.getByTestId('provider-term')).toContainText('Trial');
    await expect(page.getByTestId('provider-trial-ends')).toHaveText(trialEnd);
    await page.screenshot({ path: join(evidence, 'provider-desktop.png'), fullPage: true });
    await page.setViewportSize({ width: 390, height: 844 });
    await page.screenshot({ path: join(evidence, 'provider-mobile.png'), fullPage: true });
  } finally {
    await page.close();
  }
}

test('@prepare provider signs in with MFA and publishes the local trial policy', async ({ page }) => prepareProvider(page));

for (const phase of ['delayed', 'recovered', 'outage', 'expired']) {
  test(`@${phase} shipping billing status and merchant commerce`, async ({ browser }) => {
    const staff = await browser.newPage({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true,
      timezoneId: 'America/Los_Angeles' });
    try {
      const token = await merchantSession(staff, 'staff');
      const shown = await merchantStatus(staff, phase);
      await staff.screenshot({ path: join(evidence, `merchant-${phase}-mobile.png`), fullPage: true });
      if (phase === 'delayed') await providerStatus(browser);
      const stock = await commerce(browser, staff, token, phase);
      const privateStatus: number[] = [];
      for (const web of [merchant, admin]) {
        for (const action of ['entitlements', 'merchant-events/acknowledgement', 'merchant-events/reconciliation']) {
          const probe = await staff.request.post(`${web}/internal/licensing/v1/${action}`, { data: {} });
          if (probe.status() === 200) {
            // Merchant SPA fallback. The API-side ingress counters also stay flat.
            expect(probe.headers()['content-type']).toContain('text/html');
          } else {
            expect([404, 405]).toContain(probe.status());
          }
          privateStatus.push(probe.status());
        }
      }
      writeFileSync(join(evidence, `observations-${phase}.json`), JSON.stringify({ shown, stock, privateStatus }, null, 2));
    } finally {
      await staff.close();
    }
  });
}
