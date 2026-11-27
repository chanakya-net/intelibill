import { expect, type Page } from '@playwright/test';

/** Local PostgreSQL fixture begins with 100 accounts with low IDs; creation sorts onto page two. */
export async function verifyAccountPagination(page: Page, merchant: string): Promise<string> {
  const rows = page.getByTestId('billing-account-row');
  await page.goto(merchant + '/billing');
  await expect(rows).toHaveCount(100);
  const created = page.waitForResponse((response) =>
    new URL(response.url()).pathname === '/api/billing/accounts' && response.request().method() === 'POST',
  );
  const refreshed = page.waitForResponse((response) =>
    new URL(response.url()).pathname === '/api/billing/accounts' && new URL(response.url()).searchParams.has('after'),
  );
  await page.getByTestId('billing-account-name').fill('Account 101 after first page');
  await page.getByTestId('billing-account-create').click();
  const response = await created;
  expect(response.status()).toBe(201);
  const account = await response.json() as { accountId: string };
  const secondPage = await refreshed;
  expect(secondPage.status()).toBe(200);
  expect(await secondPage.json()).toMatchObject({ accounts: [{ accountId: account.accountId }], nextCursor: null });
  await expect(rows).toHaveCount(101);
  const newRow = page.locator(`[data-account-id="${account.accountId}"]`);
  await expect(newRow).toContainText('Account 101 after first page');

  const reloaded = page.waitForResponse((answer) => new URL(answer.url()).searchParams.has('after'));
  await page.reload();
  expect((await reloaded).status()).toBe(200);
  await expect(rows).toHaveCount(101);
  await expect(newRow).toContainText('Account 101 after first page');
  const ids = await rows.evaluateAll((elements) => elements.map((element) => element.getAttribute('data-account-id')));
  expect(new Set(ids).size).toBe(101);
  return account.accountId;
}
