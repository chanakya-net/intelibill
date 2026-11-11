import { randomUUID } from 'node:crypto';
import { test, expect, type Page } from './onboarding-fixture';

const password = 'Fixture-merchant-42!';
const product = 'Onboarding Rice';

async function openSetup(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Switch active shop' }).click();
  await page.getByRole('menuitem', { name: 'Shop setup' }).click();
  await expect(page.getByRole('heading', { name: 'Shop setup', exact: true })).toBeVisible();
}

async function choose(page: Page, control: string, option: string | RegExp): Promise<void> {
  await page.locator(control).click();
  await page.getByRole('option', { name: option }).click();
}

async function createShop(page: Page, name: string): Promise<string> {
  await page.locator('#shop-name').fill(name);
  await page.locator('#shop-address').fill('42 MG Road');
  await page.locator('#shop-city').fill('Bengaluru');
  await page.locator('#shop-state').fill('Karnataka');
  await page.locator('#shop-pincode').fill('560001');
  await choose(page, '#create-shop-state-code', /Karnataka/);
  await choose(page, '#create-shop-regime', /Not registered under GST/i);
  await page.locator('#create-shop-series').fill('R');
  const created = page.waitForResponse((response) =>
    new URL(response.url()).pathname === '/api/shops' && response.request().method() === 'POST',
  );
  await page.getByRole('button', { name: 'Create Shop', exact: true }).click();
  const response = await created;
  expect(response.ok()).toBeTruthy();
  const scope = await response.json() as { activeShopId: string };
  await expect(page).toHaveURL(/\/shops\/setup$/);
  await expect(page.getByRole('heading', { name: 'Shop setup', exact: true })).toBeVisible();
  return scope.activeShopId;
}

async function receiveOpeningStock(page: Page): Promise<void> {
  await page.getByRole('link', { name: 'Receive stock' }).click();
  await page.locator('p-autocomplete[formcontrolname="itemName"] input').fill(product);
  await page.locator('app-inventory-barcode-field input').fill('821000110014');
  await page.locator('p-inputnumber[formcontrolname="quantity"] input').fill('2.5');
  await page.locator('input[formcontrolname="uom"]').fill('kg');
  await page.locator('button.optional-details-toggle').click();
  await page.locator('input[formcontrolname="batchNumber"]').fill('onboarding-lot');
  await page.locator('p-inputnumber[formcontrolname="totalPurchaseCost"] input').fill('25');
  await page.locator('p-inputnumber[formcontrolname="mrp"] input').fill('20');
  await page.locator('p-inputnumber[formcontrolname="salesPrice"] input').fill('20');
  await choose(page, '[data-testid="purchase-tax-mode"] [role="combobox"]', 'inventory.withoutTax');
  await page.getByRole('button', { name: 'Add Row', exact: true }).click();
  const saved = page.waitForResponse((response) =>
    new URL(response.url()).pathname === '/api/inventory/inbound/batch' && response.request().method() === 'POST',
  );
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  expect((await saved).ok()).toBeTruthy();
  await openSetup(page);
}

test('verified owner previews a nonfiscal receipt and issues the first real cash receipt', async ({ page, onboarding }) => {
  const email = `onboarding-${randomUUID()}@example.test`;
  await test.step('Verify enrollment through the real account flow', async () => {
    await page.goto(`${onboarding.origin}/register`);
    await page.locator('#email').fill(email);
    const requested = page.waitForResponse((response) => new URL(response.url()).pathname === '/api/auth/enrollment/email/request');
    await page.getByRole('button', { name: 'Send verification email' }).click();
    expect((await requested).ok()).toBeTruthy();
    const { challengeId, token } = await onboarding.mailbox(email);
    const proof = new URLSearchParams({ purpose: 'enrollment', challengeId, token });
    await page.goto(`${onboarding.origin}/auth/verify-email#${proof}`);
    await page.locator('#first-name').fill('Merchant');
    await page.locator('#last-name').fill('Owner');
    await page.locator('#chosen-password').fill(password);
    await page.locator('#confirm-password').fill(password);
    await page.getByRole('button', { name: 'Complete account setup' }).click();
    await expect(page.locator('#shop-name')).toBeVisible();
  });

  const shop = await test.step('Create the shop and receive opening stock', async () => {
    const id = await createShop(page, 'Onboarding Market');
    await expect(page.getByText('Add an active product or service with a usable price and tax rate.').first()).toBeVisible();
    await receiveOpeningStock(page);
    await expect(page.getByRole('heading', { name: 'Ready to start billing' })).toBeVisible();
    await expect(page.locator('[data-step="bank"]')).toContainText('Optional');
    return id;
  });

  await test.step('Resume after reload and after signing in again', async () => {
    await page.reload();
    await expect(page.getByRole('heading', { name: 'Ready to start billing' })).toBeVisible();
    await page.getByRole('button', { name: 'Continue later' }).click();
    await expect(page).toHaveURL(/\/dashboard$/);
    await page.getByRole('button', { name: /Merchant/ }).click();
    await page.getByText('Logout', { exact: true }).click();
    await expect(page).toHaveURL(/\/login$/);
    await page.locator('input[formcontrolname="identifier"]').fill(email);
    await page.locator('p-password[formcontrolname="password"] input').fill(password);
    await page.locator('form button[type="submit"]').click();
    await expect(page).toHaveURL(/\/dashboard$/);
    await openSetup(page);
    await expect(page.getByRole('heading', { name: 'Ready to start billing' })).toBeVisible();
  });

  const before = await onboarding.snapshot(shop);
  expect(before.sales).toBe(0);
  expect(before.sequences).toBeNull();
  await test.step('Preview both formats and verify unchanged posting facts', async () => {
    await page.getByRole('link', { name: 'Preview sample receipt' }).click();
    await expect(page.getByText('SAMPLE — NOT A VALID INVOICE')).toBeVisible();
    for (const format of ['A4', 'Thermal (80 mm)']) {
      await page.getByRole('button', { name: format, exact: true }).click();
      for (const width of [390, 1280]) {
        await page.setViewportSize({ width, height: 900 });
        await page.emulateMedia({ media: 'print' });
        const receipt = page.getByRole('article');
        await expect(receipt).toContainText('Onboarding Market');
        await expect(receipt).toContainText(product);
        await expect(receipt).toContainText('1 KGS');
        await expect(receipt).toContainText('20.00');
        await expect(receipt).toContainText('SAMPLE — NOT A VALID INVOICE');
        await expect(receipt).not.toContainText(/Invoice Number|R\/\d+\/\d+/i);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        await page.emulateMedia({ media: 'screen' });
      }
    }
    await page.reload();
    await expect(page.getByText('SAMPLE — NOT A VALID INVOICE')).toBeVisible();
    expect(await onboarding.snapshot(shop)).toEqual(before);
    await page.getByRole('link', { name: 'Back to shop setup' }).click();
    await expect(page.locator('[data-step="sample"]')).toContainText('Optional');
  });

  await test.step('Post exactly one real checkout and display its issued receipt', async () => {
    await page.getByRole('button', { name: 'Start billing', exact: true }).click();
    await expect(page).toHaveURL(/\/sales\/new$/);
    await page.getByRole('combobox', { name: 'Search products' }).fill(product);
    await page.locator('app-batch-search-bar').getByRole('button', { name: /Search$/ }).click();
    await expect(page.getByRole('heading', { name: 'Cart (1)' })).toBeVisible();
    await page.getByRole('spinbutton', { name: 'Paid Amount' }).fill('20');
    await expect(page.getByText('Gross trading profit', { exact: true })).toBeVisible();
    const posted = page.waitForResponse((response) =>
      new URL(response.url()).pathname === '/api/sales' && response.request().method() === 'POST',
    );
    await page.getByRole('button', { name: /Record Sale$/ }).click();
    const response = await posted;
    expect(response.ok()).toBeTruthy();
    const sale = await response.json() as { invoiceNumber: string; document: { version: number } };
    expect(sale.invoiceNumber).toMatch(/^R\/\d{4}\/000001$/);
    expect(sale.document.version).toBe(2);
    const confirmation = page.getByRole('dialog');
    await expect(confirmation).toContainText(sale.invoiceNumber);
    const printed = page.waitForEvent('popup');
    await confirmation.getByRole('button', { name: /Print A4$/ }).click();
    const receipt = await printed;
    const issuedDocument = receipt.locator('app-sale-invoice-a4').getByRole('article');
    await expect(issuedDocument).toContainText(sale.invoiceNumber);
    await expect(issuedDocument).toContainText('Onboarding Market');
    await expect(issuedDocument).toContainText(product);
    await expect(issuedDocument).toContainText('1 KGS');
    await expect(issuedDocument).not.toContainText('SAMPLE');
    await receipt.close();
    const after = await onboarding.snapshot(shop);
    expect(after.sales).toBe(1);
    expect(after.saleItems).toBe(1);
    expect(after.saleDocuments?.[0].number).toBe(sale.invoiceNumber);
    expect(after.saleDocuments?.[0].document.version).toBe(2);
    expect(after.sequences?.[0].next_number).toBe(2);
    expect(after.batches?.[0][1]).toBe(1.5);
    expect(after.aggregate?.[0][1]).toBe(1.5);
    expect(after.stockTransactions).toBe(before.stockTransactions + 1);
  });

  await test.step('Create a second shop and resume its service-only setup without leaking the first', async () => {
    await page.getByRole('dialog').getByRole('button', { name: /Done$/ }).click();
    await page.getByRole('button', { name: /Merchant/ }).click();
    await page.getByText('Add Shop', { exact: true }).click();
    const second = await createShop(page, 'Onboarding Services');
    await page.getByRole('link', { name: 'Services', exact: true }).click();
    await page.getByRole('button', { name: 'Add Service', exact: true }).click();
    await page.locator('#add-service-name').fill('Basic repair');
    await page.locator('#add-service-price').fill('50');
    const service = page.waitForResponse((response) => new URL(response.url()).pathname === '/api/services' && response.request().method() === 'POST');
    await page.getByRole('dialog').getByRole('button', { name: 'Add Service', exact: true }).click();
    expect((await service).ok()).toBeTruthy();
    await openSetup(page);
    await expect(page.getByRole('heading', { name: 'Ready to start billing' })).toBeVisible();
    await expect(page.locator('[data-step="stock"]')).toContainText('Not applicable');
    await expect(page.getByRole('link', { name: 'Receive stock' })).toHaveCount(0);
    await page.getByRole('link', { name: 'Preview sample receipt' }).click();
    await expect(page.getByRole('article')).toContainText('Basic repair');
    await expect(page.getByRole('article')).not.toContainText(product);
    await page.getByRole('button', { name: 'I checked this sample' }).click();
    await expect(page.getByText('Sample checked')).toBeVisible();
    expect((await onboarding.snapshot(second)).sales).toBe(0);
    await page.getByRole('link', { name: 'Back to shop setup' }).click();

    await page.getByRole('button', { name: 'Switch active shop' }).click();
    await page.getByRole('menuitem', { name: /Onboarding Market/ }).click();
    await expect(page).toHaveURL(/\/dashboard$/);
    await openSetup(page);
    await expect(page.locator('[data-step="sample"]')).toContainText('Optional');
    await expect(page.locator('[data-step="stock"]')).toContainText('Complete');
  });
});
