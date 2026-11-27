import { expect, test, type Page, type Route } from '@playwright/test';
import {
  add, count, endpoint, enterRow, finish, frozen, intakeResult, name, openSibling, records, save, seedRows, withIntake,
} from './licensing-stock-intake-fixtures';

const table = (page: Page) => page.locator('.table-card');
const rowButton = (page: Page, id: string, action: 'Edit' | 'Remove') =>
  page.locator('.desktop-pending-table tr', { hasText: 'Fixture goods ' + id }).getByRole('button', { name: action });

/** The barcodes of the rows each stored draft holds. Fixture rows use their id as barcode. */
async function pendingIds(page: Page) {
  const drafts = await records(page, 'inventory-inbound-batch') as { rows: { barcode: string }[] }[];
  return drafts.map(draft => draft.rows.map(row => row.barcode));
}

async function staleChange(page: Page, action: 'Remove' | 'Edit' | 'Add Row') {
  if (action !== 'Add Row') return rowButton(page, 'B', action).click();
  await enterRow(page, 'C');
  await add(page).click();
}

/** A is confirmed (or replayed) and B is refused with the given row error. */
function partialResult(route: Route, code: string) {
  const result = intakeResult(route, 1);
  return { ...result, failedCount: 1, failed: [{ clientRowId: 'B', itemName: 'Fixture goods B', barcode: 'B',
    retryable: false, errors: [{ code, description: 'fixture row refusal' }] }] };
}

for (const action of ['Remove', 'Edit', 'Add Row'] as const) {
  test(`R007 a stale ${action} after a partial save leaves only B pending`, async ({ page }) => {
    await withIntake(page, async origin => {
      const posts: string[] = [];
      let refusal = 'Licence.Expired';
      await page.context().route(endpoint, async route => {
        posts.push(route.request().postData()!);
        await route.fulfill({ status: 207, json: partialResult(route, refusal) });
      });
      await seedRows(page);
      await page.goto(origin + '/inventory/batch');
      await expect(count(page)).toHaveText('Rows: 2');
      const stale = await openSibling(page, origin);
      await expect(count(stale)).toHaveText('Rows: 2');

      await save(page).click();
      await expect(finish(page)).toBeEnabled();
      await expect(count(page)).toHaveText('Rows: 1');
      const original = await frozen(page);
      expect(original).toHaveLength(1);
      expect(await pendingIds(page)).toEqual([['B']]);

      // The sibling still shows A+B and no interrupted save.
      await expect(finish(stale)).toHaveCount(0);
      await staleChange(stale, action);
      await expect(table(stale)).not.toContainText('Fixture goods A');
      await expect(table(stale)).toContainText('Fixture goods B');
      await expect(count(stale)).toHaveText('Rows: 1');
      await expect(finish(stale)).toBeEnabled();
      // A row the frozen save cannot take stays in the form; an Edit that lost the race leaves it empty.
      await expect(name(stale)).toHaveValue(action === 'Add Row' ? 'Fixture goods C' : '');
      expect(await pendingIds(stale)).toEqual([['B']]);
      expect(await frozen(stale)).toEqual(original);
      expect(posts).toHaveLength(1);

      await stale.reload();
      await expect(finish(stale)).toBeEnabled();
      await expect(count(stale)).toHaveText('Rows: 1');
      await expect(table(stale)).toContainText('Fixture goods B');
      expect(await pendingIds(stale)).toEqual([['B']]);
      expect(await frozen(stale)).toEqual(original);

      // Recovery replays A and returns a definitive, editable rejection for B.
      refusal = 'Inventory.InvalidQuantity';
      await finish(stale).click();
      await expect(finish(stale)).toHaveCount(0);
      await expect(count(stale)).toHaveText('Rows: 1');
      expect(posts).toHaveLength(2);
      expect(posts[1]).toBe(posts[0]);
      expect(await frozen(stale)).toEqual([]);
      expect(await pendingIds(stale)).toEqual([['B']]);
      await rowButton(stale, 'B', 'Edit').click();
      await expect(name(stale)).toHaveValue('Fixture goods B');
    });
  });
}

test('R007 stale changes join the rows another tab kept', async ({ page }) => {
  await withIntake(page, async origin => {
    await seedRows(page);
    await page.goto(origin + '/inventory/batch');
    await expect(count(page)).toHaveText('Rows: 2');
    const stale = await openSibling(page, origin);
    await expect(count(stale)).toHaveText('Rows: 2');

    await rowButton(page, 'A', 'Remove').click();
    await expect(count(page)).toHaveText('Rows: 1');
    expect(await pendingIds(page)).toEqual([['B']]);

    // A new row is added after the rows the sibling kept. It does not bring A back.
    await enterRow(stale, 'C');
    await add(stale).click();
    await expect(count(stale)).toHaveText('Rows: 2');
    await expect(table(stale)).toContainText('Fixture goods C');
    await expect(table(stale)).not.toContainText('Fixture goods A');
    await expect(name(stale)).toHaveValue('');
    expect(await pendingIds(stale)).toEqual([['B', 'C']]);

    // The first tab is stale now. Its removal is refused and it shows the stored rows instead.
    await rowButton(page, 'B', 'Remove').click();
    await expect(count(page)).toHaveText('Rows: 2');
    await expect(table(page)).toContainText('Fixture goods C');
    expect(await pendingIds(page)).toEqual([['B', 'C']]);
    await rowButton(page, 'B', 'Remove').click();
    await expect(count(page)).toHaveText('Rows: 1');
    expect(await pendingIds(page)).toEqual([['C']]);
  });
});
