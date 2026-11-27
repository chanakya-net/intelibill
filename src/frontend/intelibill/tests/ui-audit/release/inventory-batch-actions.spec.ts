import { expect, test, type Page } from '@playwright/test';
import { resolve } from 'node:path';
import { session, SHOP_A, USER, startPurchaseReceivingServer } from './purchase-receiving-server';
import {
  batch, targetBatch, json, receiptDrafts, installReceiptFixture, openReceiptCorrection,
  enterReceiptCorrection, switchReceiptShop,
} from './inventory-batch-actions-fixture';

test.use({ serviceWorkers: 'block', trace: 'on' });

const restricted = (page: Page) => page.getByTestId('shop-data-restricted');

async function expectReceiptRestriction(page: Page) {
  await expect(restricted(page)).toHaveText('Security suspension is enforced for this shop.');
  await expect(page.locator('section.batches-page')).toHaveCount(0);
  await expect(page.getByTestId('receipt-cost-recovery')).toHaveCount(0);
  await expect(page.getByText(batch.batchNumber, { exact: true })).toHaveCount(0);
  await expect(page.getByText(targetBatch.itemName, { exact: true })).toHaveCount(0);
}

async function revalidateReceiptAccess(page: Page) {
  const authority = page.waitForResponse(
    (response) => response.url().endsWith(`/api/licensing/shops/${SHOP_A}/trading-authority`),
  );
  await page.bringToFront();
  await page.evaluate(() => window.dispatchEvent(new Event('online')));
  const response = await authority;
  expect(response.status()).toBe(403);
  expect(await response.json()).toMatchObject({ code: 'Licence.Expired', enforced: true });
  await expect(restricted(page)).toHaveCount(0);
}

for (const width of [1440, 390]) {
  test(`batch menu pointer actions at ${width}px`, async ({ page }) => {
    const server = await startPurchaseReceivingServer(
      resolve(__dirname, '../../../dist/INVENTORY/browser'),
    );
    await page.setViewportSize({ width, height: 1000 });
    await page.addInitScript(
      (value) => localStorage.setItem('inventory.auth.session.local', JSON.stringify(value)),
      session(),
    );
    const voids: string[] = [];
    await page.route('**/api/inventory/batches', (route) => json(route, [batch, targetBatch]));
    await page.route('**/api/inventory/batches/batch-target/void', (route) => {
      voids.push(route.request().method());
      return route.fulfill({ status: 204 });
    });
    try {
      for (const label of [
        'Adjust Batch',
        'Count stock',
        'Edit prices & details',
        'Correct receipt cost',
        'Print labels',
        'Void',
      ]) {
        await page.goto(`${server.origin}/inventory/batches`);
        await page
          .getByRole('button', { name: 'Actions', exact: true })
          .filter({ visible: true })
          .last()
          .click();
        const item = page.locator('.p-menu:visible').getByText(label, { exact: true });
        await expect(item).toBeVisible();
        // Exercise pointer focus/blur between down and up, like a physical click.
        await item.click({ delay: 80 });
        if (label === 'Void') await expect.poll(() => voids).toEqual(['POST']);
        else {
          const dialog = page.getByRole('dialog');
          await expect(dialog).toBeVisible();
          await expect(dialog).toContainText(
            label === 'Print labels' ? targetBatch.itemName : targetBatch.batchNumber,
          );
          if (label !== 'Print labels') await expect(dialog).toHaveAccessibleName(label);
        }
      }
      await page.goto(`${server.origin}/inventory/batches`);
      const actions = page
        .getByRole('button', { name: 'Actions', exact: true })
        .filter({ visible: true });
      await actions.first().click();
      await expect(page.getByRole('menu')).toBeVisible();
      await actions.last().click();
      await expect(page.getByRole('menu')).toBeVisible();
      await page.getByRole('menuitem', { name: 'Adjust Batch', exact: true }).click({ delay: 80 });
      await expect(page.getByRole('dialog', { name: 'Adjust Batch', exact: true })).toContainText(
        targetBatch.batchNumber,
      );
      await page.goto(`${server.origin}/inventory/batches`);
      await actions.first().click();
      await expect(page.getByRole('menu')).toBeVisible();
      await actions.first().click();
      await expect(page.getByRole('menu')).toBeHidden();
      // Reopening for another row refreshes the shared popup's target.
      await actions.first().focus();
      await page.keyboard.press('Enter');
      const menu = page.getByRole('menu');
      await expect(menu).toBeFocused();
      await page.keyboard.press('Home');
      await expect(menu).toHaveAttribute('aria-activedescendant', 'printLabels');
      await page.keyboard.press('ArrowDown');
      await expect(menu).toHaveAttribute('aria-activedescendant', 'adjust');
      await page.keyboard.press('Enter');
      await expect(page.getByRole('dialog', { name: 'Adjust Batch', exact: true })).toContainText(
        batch.batchNumber,
      );
    } finally {
      await server.close();
    }
  });
}

test.describe('P04-13d receipt correction recovery', () => {
  const build = resolve(__dirname, '../../../dist/INVENTORY/browser');
  const check = (page: Page) => page.getByTestId('receipt-cost-recovery').getByRole('button', { name: 'Check correction outcome' });

  test('P04-13d desktop restores a lost correction across shops and confirms without reposting', async ({ page, context }, info) => {
    const server = await startPurchaseReceivingServer(build);
    const url = `${server.origin}/inventory/batches`;
    const state = await installReceiptFixture(page);
    state.dropNext = true;
    await page.setViewportSize({ width: 1440, height: 1000 });
    try {
      await page.goto(url);
      const dialog = await enterReceiptCorrection(page, '500');
      await expect(dialog.getByText('This shop’s trading access has ended.')).toBeVisible();
      await expect(dialog.getByTestId('receipt-cost-delta')).toContainText('+₹100.00');
      await page.screenshot({ animations: 'disabled', path: info.outputPath('signed-delta-desktop.png') });
      await dialog.getByRole('button', { name: 'Post correction' }).click();
      await expect(check(page)).toBeEnabled();
      expect(state.posts).toHaveLength(1);
      const frozen = state.posts[0];
      const pendingDraft = {
        userId: USER, shopId: SHOP_A, targetId: 'receipt-menu:3',
        pending: { status: 'unknown', operationKey: frozen.operationKey, payload: frozen },
      };
      await expect.poll(() => receiptDrafts(page)).toMatchObject([pendingDraft]);

      await page.reload();
      await expect(check(page)).toBeEnabled();
      await switchReceiptShop(page, 'Shop B');
      await page.goto(url);
      await expect(check(page)).toHaveCount(0);
      await switchReceiptShop(page, 'Shop A');
      state.rows = [targetBatch];
      await page.goto(url);
      await expect(check(page)).toBeEnabled();

      const otherTab = await context.newPage();
      const otherState = await installReceiptFixture(otherTab);
      await otherTab.goto(url);
      await expect(check(otherTab)).toBeEnabled();

      await expect(page.getByText(targetBatch.itemName, { exact: true }).filter({ visible: true }).first()).toBeVisible();
      state.lookup = 'denied';
      state.security = otherState.security = true;
      const denied = page.waitForResponse(
        (response) => response.url().endsWith(`/api/financial-operations/receipt-cost-correction/${frozen.operationKey}`),
      );
      await check(page).click();
      const refusal = await denied;
      expect(refusal.status()).toBe(403);
      expect(await refusal.json()).toMatchObject({ code: 'Licence.SecuritySuspended' });
      await expectReceiptRestriction(page);
      await expectReceiptRestriction(otherTab);
      expect(state.posts).toHaveLength(1);
      await expect.poll(() => receiptDrafts(page)).toMatchObject([pendingDraft]);
      expect((await receiptDrafts(page))[0].pending?.payload).toEqual(JSON.parse(state.postBodies[0]));
      await page.screenshot({ animations: 'disabled', path: info.outputPath('security-preserves-intent.png') });

      state.security = otherState.security = false;
      await revalidateReceiptAccess(page);
      await expect(check(page)).toBeEnabled();
      await revalidateReceiptAccess(otherTab);
      await expect(check(otherTab)).toBeEnabled();
      state.lookup = 'mismatch';
      await check(page).click();
      await expect.poll(() => state.sequence.at(-1)).toBe('GET mismatch');
      await expect(check(page)).toBeEnabled();
      expect(state.posts).toHaveLength(1);
      expect((await receiptDrafts(page))[0].pending?.payload).toEqual(frozen);

      state.lookup = 'match';
      state.rows = [{ ...batch, receiptVersion: 4, receiptTotalCost: 500 }];
      await check(page).click();
      await expect(check(page)).toHaveCount(0);
      await expect(check(otherTab)).toHaveCount(0);
      await expect.poll(() => receiptDrafts(page)).toEqual([]);
      expect(state.sequence).toEqual(['POST', 'GET denied', 'GET mismatch', 'GET match']);
      expect(otherState.posts).toEqual([]);
      console.log('P04-13d desktop 1440px: persisted key suffix %s; original owner recovery after reload and shop switch; POST → denied GET → mismatched GET → matching GET; zero resends.', frozen.operationKey.slice(-8));

      const later = await enterReceiptCorrection(page, '450');
      await expect(later.getByTestId('receipt-cost-delta')).toContainText('-₹50.00');
      await later.getByRole('button', { name: 'Post correction' }).click();
      await expect(later).toBeHidden();
      expect(state.posts[1].operationKey).not.toBe(frozen.operationKey);
      expect(state.posts[1].expectedVersion).toBe(4);
      await page.screenshot({ animations: 'disabled', path: info.outputPath('confirmed-desktop.png') });
    } finally {
      await server.close();
    }
  });

  test('P04-13d narrow viewport only resends identical frozen JSON after a typed miss', async ({ page }, info) => {
    const server = await startPurchaseReceivingServer(build);
    const state = await installReceiptFixture(page);
    state.dropNext = true;
    state.tradingCode = 'Licence.TradingRestricted';
    await page.setViewportSize({ width: 390, height: 844 });
    try {
      await page.goto(`${server.origin}/inventory/batches`);
      const dialog = await enterReceiptCorrection(page, '500');
      await expect(dialog.getByText('Trading is restricted for this shop.')).toBeVisible();
      await expect(dialog.getByTestId('receipt-cost-delta')).toContainText('+₹100.00');
      await page.screenshot({ animations: 'disabled', path: info.outputPath('signed-delta-narrow.png') });
      await dialog.getByRole('button', { name: 'Post correction' }).click();
      await expect(check(page)).toBeEnabled();
      await page.reload();
      await expect(check(page)).toBeEnabled();

      state.lookup = 'other-404';
      await check(page).click();
      await expect.poll(() => state.sequence.at(-1)).toBe('GET other-404');
      await expect(check(page)).toBeEnabled();
      expect(state.posts).toHaveLength(1);

      state.lookup = 'miss';
      await check(page).click();
      await expect(check(page)).toHaveCount(0);
      expect(state.posts).toHaveLength(2);
      expect(state.postBodies[1]).toBe(state.postBodies[0]);
      expect(state.sequence).toEqual(['POST', 'GET other-404', 'GET miss', 'POST']);
      console.log('P04-13d narrow 390px: POST → unrelated 404 GET → Operation.NotFound GET → one identical POST; key suffix %s.', state.posts[0].operationKey.slice(-8));
      await page.screenshot({ animations: 'disabled', path: info.outputPath('confirmed-narrow.png') });
    } finally {
      await server.close();
    }
  });

  test('P04-13d a definitive stale resend releases the old intent for a new correction', async ({ page }, info) => {
    const server = await startPurchaseReceivingServer(build);
    const state = await installReceiptFixture(page);
    state.dropNext = true;
    try {
      await page.goto(`${server.origin}/inventory/batches`);
      const first = await enterReceiptCorrection(page, '500');
      await first.getByRole('button', { name: 'Post correction' }).click();
      await expect(check(page)).toBeEnabled();
      const frozen = state.posts[0];
      await page.reload();
      await expect(check(page)).toBeEnabled();

      state.lookup = 'miss';
      state.conflictNext = true;
      state.rows = [{ ...batch, receiptVersion: 4, receiptTotalCost: 500 }];
      await check(page).click();
      await expect(check(page)).toHaveCount(0);
      expect(state.sequence).toEqual(['POST', 'GET miss', 'POST']);
      expect(state.postBodies[1]).toBe(state.postBodies[0]);
      await page.screenshot({ path: info.outputPath('definitive-version-conflict.png') });

      await page.reload();
      await expect.poll(() => receiptDrafts(page)).toMatchObject([{ targetId: 'receipt-menu:3', pending: null }]);
      await expect(check(page)).toHaveCount(0);
      const later = await enterReceiptCorrection(page, '450');
      await expect(later.getByTestId('receipt-cost-delta')).toContainText('-₹50.00');
      await later.getByRole('button', { name: 'Post correction' }).click();
      await expect(later).toBeHidden();
      expect(state.posts).toHaveLength(3);
      expect(state.posts[2].expectedVersion).toBe(4);
      expect(state.posts[2].operationKey).not.toBe(frozen.operationKey);
      console.log('Stale resend reused key suffix %s. Explicit version 4 correction used distinct key suffix %s.',
        frozen.operationKey.slice(-8), state.posts[2].operationKey.slice(-8));
    } finally {
      await server.close();
    }
  });

  for (const read of ['denied', 'failed', 'authorized'] as const) {
    test(`P04-13d cached lot stays hidden pending the ${read} shop read`, async ({ page }, info) => {
      const server = await startPurchaseReceivingServer(build);
      const state = await installReceiptFixture(page);
      state.dropNext = true;
      const readGate = Promise.withResolvers<void>();
      try {
        await page.goto(`${server.origin}/inventory/batches`);
        const dialog = await enterReceiptCorrection(page, '500');
        await dialog.getByRole('button', { name: 'Post correction' }).click();
        await expect(check(page)).toBeEnabled();
        await page.route('**/api/inventory/batches', async (route) => {
          await readGate.promise;
          if (read === 'failed') return route.abort('failed');
          if (read === 'denied' && state.security)
            return route.fulfill({ status: 403, json: { code: 'Licence.SecuritySuspended' } });
          return json(route, state.rows);
        });
        const loading = page.waitForRequest('**/api/inventory/batches');
        await page.reload();
        await loading;
        const banner = page.getByTestId('receipt-cost-recovery');
        await expect(banner).toBeVisible();
        await expect(page.getByText(batch.batchNumber, { exact: true })).toHaveCount(0);
        await page.screenshot({ path: info.outputPath('read-pending.png') });

        const finished = read === 'failed'
          ? page.waitForEvent('requestfailed', { predicate: (request) => request.url().endsWith('/api/inventory/batches') })
          : page.waitForResponse('**/api/inventory/batches').then(async (response) => {
            if (read === 'denied') {
              expect(response.status()).toBe(403);
              expect(await response.json()).toMatchObject({ code: 'Licence.SecuritySuspended' });
            }
          });
        if (read === 'denied') state.security = true;
        readGate.resolve();
        await finished;
        if (read === 'denied') {
          await expectReceiptRestriction(page);
          const frozen = state.posts[0];
          await expect.poll(() => receiptDrafts(page)).toMatchObject([{
            userId: USER, shopId: SHOP_A, targetId: 'receipt-menu:3',
            pending: { status: 'unknown', operationKey: frozen.operationKey, payload: frozen },
          }]);
          expect((await receiptDrafts(page))[0].pending?.payload).toEqual(JSON.parse(state.postBodies[0]));
          expect(state.posts).toHaveLength(1);
          await page.screenshot({ path: info.outputPath('read-denied.png') });

          state.security = false;
          await revalidateReceiptAccess(page);
          await expect(banner).toContainText(batch.batchNumber);
          await expect(check(page)).toBeEnabled();
          state.rows = [{ ...batch, receiptVersion: 4, receiptTotalCost: 500 }];
          await check(page).click();
          await expect(check(page)).toHaveCount(0);
          await expect.poll(() => receiptDrafts(page)).toEqual([]);
          expect(state.sequence).toEqual(['POST', 'GET match']);
          return;
        }
        if (read === 'authorized') {
          await expect(banner).toContainText(batch.batchNumber);
        } else {
          await expect(page.locator('section.batches-page [role=alert]').filter({ hasText: 'Failed to load inventory batches' })).toBeVisible();
          await expect(page.getByText(batch.batchNumber, { exact: true })).toHaveCount(0);
        }
        await expect(check(page)).toBeEnabled();
        await expect.poll(() => receiptDrafts(page)).toMatchObject([{
          targetId: 'receipt-menu:3', pending: { status: 'unknown', payload: state.posts[0] },
        }]);
        expect(state.posts).toHaveLength(1);
        await page.screenshot({ path: info.outputPath(`read-${read}.png`) });
      } finally {
        readGate.resolve();
        await server.close();
      }
    });
  }
});
