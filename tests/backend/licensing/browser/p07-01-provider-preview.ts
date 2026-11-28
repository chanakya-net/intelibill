import { join } from 'node:path';
import { expect, type Page } from '@playwright/test';
import { adminWeb, cfg, createDelayedRouteGate, evidence, inspectProvider, settleFrames, signalHost } from './p07-01-notice-fixture';

export async function verifyBillingOperatorWithStaleCommercialRace(page: Page) {
  await page.goto(`${adminWeb}/accounts`);
  const previewReplyPromise = page.waitForResponse(
    (r) => r.url().includes(`/api/admin/accounts/${cfg.accountA}/notifications`) && r.request().method() === 'GET',
  );
  await page.getByLabel('Billing account ID').fill(cfg.accountA);
  await page.getByRole('button', { name: 'Read access status' }).click();
  const previewReply = await previewReplyPromise;
  expect(previewReply.status()).toBe(200);
  expect(previewReply.headers()['cache-control'] ?? '').toContain('no-store');
  expect((await previewReply.json()).commercialIncluded).toBe(true);

  const panel = page.getByTestId('provider-notifications-panel');
  await expect(panel.getByTestId('provider-notification-row').first()).toBeVisible();
  await expect(panel.getByTestId('provider-notification-due-utc').first()).toContainText('22 Feb 2028, 04:30:00 UTC');
  await expect(panel.getByTestId('provider-notification-due-ist').first()).toContainText('22 Feb 2028, 10:00:00 IST');
  await expect(panel.getByTestId('provider-notification-price').first()).toContainText('120');

  const renderedIds = await panel.getByTestId('provider-notification-row').evaluateAll((nodes) =>
    nodes.map((n) => n.getAttribute('data-job-id')),
  );
  for (const id of renderedIds) expect(cfg.expectedJobIds).toContain(id);
  const badges = await panel.getByTestId('provider-notification-due-badge').allTextContents();
  expect(badges.some((text) => text.trim() === 'Due')).toBe(true);
  expect(badges.some((text) => text.trim() === 'Scheduled (future)')).toBe(true);

  const localeSelect = panel.getByTestId('provider-notifications-locale');
  await localeSelect.focus();
  await expect(localeSelect).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(panel.getByTestId('provider-notifications-refresh')).toBeFocused();
  await localeSelect.selectOption('hi-IN');
  await expect(panel.locator('#provider-notifications-heading')).toContainText('निर्धारित लाइसेंस सूचनाएं');
  await localeSelect.selectOption('en-IN');
  await expect(panel.locator('#provider-notifications-heading')).toContainText('Scheduled licence notifications');
  await page.screenshot({ path: join(evidence, 'admin-billing-commercial-preview.png'), fullPage: true });

  await page.setViewportSize({ width: 390, height: 844 });
  const scrollWidth = await page.evaluate(() => document.documentElement.scrollWidth);
  expect(scrollWidth).toBeLessThanOrEqual(390);
  await page.screenshot({ path: join(evidence, 'admin-billing-390px.png'), fullPage: true });

  const race = await verifyRevokedRefresh(page);
  return { commercialIncluded: true, renderedJobCount: renderedIds.length, narrowScrollWidth: scrollWidth,
    ...race, browserLanguagesSwitched: ['hi-IN', 'en-IN'] };
}

async function verifyRevokedRefresh(page: Page) {
  const panel = page.getByTestId('provider-notifications-panel');
  // Deterministic race: hold a commercial refresh, revoke capability, fire focus revalidation (403), then release old commercial reply.
  const gate = await createDelayedRouteGate(page, `**/api/admin/accounts/${cfg.accountA}/notifications**`, inspectProvider);
  await panel.getByTestId('provider-notifications-refresh').click();
  const held = await gate.intercepted;
  expect(gate.hits()).toBe(1);

  await signalHost('revoke-billing-capability', 'billing-revoked', 'revoke');
  const deniedReply = page.waitForResponse(
    (r) => r.url().includes(`/api/admin/accounts/${cfg.accountA}/notifications`) && [401, 403].includes(r.status()),
  );
  await page.evaluate(() => window.dispatchEvent(new Event('focus')));
  const deniedStatus = (await deniedReply).status();
  expect([401, 403]).toContain(deniedStatus);
  await expect(panel.getByTestId('provider-notification-row')).toHaveCount(0);

  gate.release();
  const staleAdminOutcome = await gate.settled;
  await settleFrames(page);
  expect(['fulfilled', 'aborted']).toContain(staleAdminOutcome);
  await expect(panel.getByTestId('provider-notification-row')).toHaveCount(0);
  await expect(panel.getByTestId('provider-notification-commercial')).toHaveCount(0);
  await gate.cleanup();

  return { staleCommercialAfterRevocationOutcome: staleAdminOutcome, held, deniedStatus, finalCommercialRows: 0 };
}
