import { expect, test, type Page } from '@playwright/test';

import { createDashboardScenario, installDashboardFixture } from '../fixtures/dashboard.fixture';
import { waitForStablePage } from '../support/audit-page';

test.describe('shop dropdowns', () => {
  test.beforeEach(async ({ page }) => {
    await installDashboardFixture(page, createDashboardScenario());
    await page.clock.setFixedTime(new Date('2026-07-29T09:30:00+05:30'));
    await page.goto('/dashboard');
    await waitForStablePage(page);
  });

  for (const label of ['Add Shop', 'Manage Shop'] as const) {
    test(`reaches the final ${label} action with the mouse wheel`, async ({ page }) => {
      await openShopOverlay(page, label);
      const root = page.locator(
        label === 'Add Shop' ? '.create-shop-overlay' : '.manage-shop-overlay',
      );
      const card = root.locator('.overlay-card');
      const action =
        label === 'Add Shop'
          ? root.locator('button[type="submit"]')
          : root.locator('button[form="manage-shop-details-form"]');
      await expect(root.locator('.overlay-form').first()).toBeVisible();
      const body = await root.locator('.overlay-form').first().boundingBox();
      const box = await card.boundingBox();
      await page.mouse.move(
        box!.x + box!.width / 2,
        Math.min(body!.y + 100, box!.y + box!.height - 40),
      );
      // Locator.click/Tab would auto-scroll hidden ancestors and conceal this regression.
      // Firefox caps each wheel gesture to a page, so repeat real gestures until the end.
      await expect
        .poll(async () => {
          await page.mouse.wheel(0, 5000);
          return action.evaluate((element) => {
            const rect = element.getBoundingClientRect();
            const cardRect = element.closest('.overlay-card')!.getBoundingClientRect();
            const hit = document.elementFromPoint(
              rect.x + rect.width / 2,
              rect.y + rect.height / 2,
            );
            return (
              rect.top >= cardRect.top &&
              rect.bottom <= cardRect.bottom &&
              rect.top >= 0 &&
              rect.bottom <= innerHeight &&
              !!hit &&
              element.contains(hit)
            );
          });
        })
        .toBe(true);

      await expect
        .poll(async () => {
          await page.mouse.wheel(0, -5000);
          return root
            .locator('#shop-name, #manage-shop-id')
            .first()
            .evaluate((element) => {
              const rect = element.getBoundingClientRect();
              const hit = document.elementFromPoint(
                rect.x + rect.width / 2,
                rect.y + rect.height / 2,
              );
              return !!hit && element.contains(hit);
            });
        })
        .toBe(true);
    });
  }

  test('selects GST state, registration and e-invoicing in Add Shop', async ({ page }) => {
    await openShopOverlay(page, 'Add Shop');
    const form = page.locator('.create-shop-overlay');
    await form.locator('p-select[formcontrolname="stateCode"]').click();
    const popup = page.locator('.p-select-overlay');
    await expect(popup).toBeVisible();
    await popup.locator('input').fill('Karnataka');
    // A normal pointer click fails when the options are behind the custom modal.
    await page
      .getByRole('option', { name: 'Karnataka (29)', exact: true })
      .click({ timeout: 5000 });
    await expect(form.locator('p-select[formcontrolname="stateCode"]')).toContainText(
      'Karnataka (29)',
    );

    await form.locator('p-select[formcontrolname="taxRegime"]').click();
    await page.getByRole('option', { name: 'Regular GST registration', exact: true }).click();
    await form.locator('p-select[formcontrolname="eInvoiceStatus"]').click();
    await page.getByRole('option', { name: 'Exempt from e-invoicing', exact: true }).click();
    await expect(form.locator('p-select[formcontrolname="eInvoiceStatus"]')).toContainText(
      'Exempt from e-invoicing',
    );

    await form.locator('p-select[formcontrolname="taxRegime"]').click();
    await page.getByRole('option', { name: 'Not registered under GST', exact: true }).click();
    await expect(form.locator('p-select[formcontrolname="eInvoiceStatus"]')).toHaveCount(0);
    await form.locator('#shop-name').fill('Dropdown Test Shop');
    await form.locator('#shop-address').fill('123 Test Road');
    await form.locator('#shop-city').fill('Bengaluru');
    await form.locator('#shop-state').fill('Karnataka');
    await form.locator('#shop-pincode').fill('560001');

    // Refuse the mocked write after observing the payload; no real shop is created.
    await page.route('http://localhost:5277/api/shops', (route) =>
      route.fulfill({
        status: 422,
        json: { title: 'UiAudit.DeclaredShopError' },
      }),
    );
    const submitted = page.waitForRequest(
      (request) =>
        request.method() === 'POST' && request.url() === 'http://localhost:5277/api/shops',
    );
    await form.locator('button[type="submit"]').click();
    expect((await submitted).postDataJSON().taxProfile).toMatchObject({
      stateCode: '29',
      taxRegime: 'Unregistered',
      eInvoiceStatus: null,
    });
  });

  test('selects GST options with pointer and keyboard in Manage Shop', async ({ page }) => {
    await openShopOverlay(page, 'Manage Shop');
    const form = page.locator('app-shop-tax-profile-form');
    await form.locator('p-select[formcontrolname="stateCode"]').click();
    await page.locator('.p-select-overlay input').fill('Delhi');
    await page.getByRole('option', { name: 'Delhi (07)', exact: true }).click({ timeout: 5000 });
    await expect(form.locator('p-select[formcontrolname="stateCode"]')).toContainText('Delhi (07)');
    await form.locator('p-select[formcontrolname="taxRegime"]').click();
    await page.getByRole('option', { name: 'Regular GST registration', exact: true }).click();
    const eInvoice = form.locator('#tax-profile-einvoice-input');
    await eInvoice.focus();
    await eInvoice.press('ArrowDown');
    await expect(page.getByRole('listbox')).toBeVisible();
    await eInvoice.press('Home');
    await eInvoice.press('Enter');
    await expect(form.locator('p-select[formcontrolname="eInvoiceStatus"]')).toContainText(
      'Turnover is below the e-invoicing limit',
    );
  });

  test('selects a bank account type in the shop setup overlay', async ({ page }) => {
    await openShopOverlay(page, 'Add Shop');
    const form = page.locator('.create-shop-overlay');
    await form.locator('p-select[formcontrolname="stateCode"]').click();
    await page.locator('.p-select-overlay input').fill('Karnataka');
    await page.getByRole('option', { name: 'Karnataka (29)', exact: true }).click();
    await form.locator('p-select[formcontrolname="taxRegime"]').click();
    await page.getByRole('option', { name: 'Not registered under GST', exact: true }).click();
    await form.locator('#shop-name').fill('Dropdown Test Shop');
    await form.locator('#shop-address').fill('123 Test Road');
    await form.locator('#shop-city').fill('Bengaluru');
    await form.locator('#shop-state').fill('Karnataka');
    await form.locator('#shop-pincode').fill('560001');
    const session = createDashboardScenario().shell.session;
    await page.route('http://localhost:5277/api/shops', (route) =>
      route.fulfill({
        json: {
          accessToken: session.accessToken,
          accessTokenExpiresAt: session.accessTokenExpiresAt,
          activeShopId: session.activeShopId,
          shops: session.shops,
          scopeVersion: 1,
        },
      }),
    );
    await form.locator('button[type="submit"]').click();
    await expect(form.locator('#create-bank-name')).toBeInViewport({ ratio: 1 });
    await form.locator('p-select[formcontrolname="accountType"]').click();
    await page.getByRole('option').filter({ hasText: 'Savings' }).click({ timeout: 5000 });
    await expect(form.locator('p-select[formcontrolname="accountType"]')).toContainText('Savings');
    await form.getByRole('button', { name: /Previous/ }).click();
    await expect(form.locator('#shop-name')).toBeInViewport({ ratio: 1 });
  });

  test('starts each Manage Shop step at the top after next and previous', async ({ page }) => {
    await openShopOverlay(page, 'Manage Shop');
    const form = page.locator('.manage-shop-overlay');
    await form.locator('button[form="manage-shop-details-form"]').click();
    await expect(form.locator('#manage-bank-name')).toBeInViewport({ ratio: 1 });
    await form.getByRole('button', { name: /Previous/ }).click();
    await expect(form.locator('#manage-shop-id')).toBeInViewport({ ratio: 1 });
  });
});

async function openShopOverlay(page: Page, label: 'Add Shop' | 'Manage Shop'): Promise<void> {
  const mobile = page.viewportSize()!.width <= 1024;
  if (mobile) await page.locator('.mobile-menu-trigger').click();
  const profile = page.locator(
    mobile
      ? '.mobile-nav-drawer .mobile-profile-section'
      : '.shell-sidebar .shell-panelmenu-profile',
  );
  await profile.locator('.p-panelmenu-header-link').click();
  await profile.getByText(label, { exact: true }).click();
}
