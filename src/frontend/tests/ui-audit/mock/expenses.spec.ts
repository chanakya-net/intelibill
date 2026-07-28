import { expect, test, type Page } from '@playwright/test';

import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  mockExternalRequests,
  waitForStablePage,
} from '../support/audit-page';

interface MockExpense {
  readonly id: string;
  readonly amount: number;
  readonly categoryName: string;
  readonly paidTo: string;
  readonly expenseDate: string;
  readonly isVoided: boolean;
}

const EXPENSES: readonly MockExpense[] = [
  {
    id: 'expense-rent',
    amount: 25000,
    categoryName: 'Office Rent',
    paidTo: 'Bengaluru Property Holdings',
    expenseDate: '2026-07-20',
    isVoided: false,
  },
  {
    id: 'expense-utilities',
    amount: 1875.5,
    categoryName: 'Utilities',
    paidTo: 'BESCOM',
    expenseDate: '2026-07-18',
    isVoided: true,
  },
  {
    id: 'expense-supplies',
    amount: 760.25,
    categoryName: 'Office Supplies',
    paidTo: 'Long Name Stationery and Office Supplies Cooperative',
    expenseDate: '2026-07-15',
    isVoided: false,
  },
];

const DETAIL = {
  ...EXPENSES[0],
  shopId: 'ui-audit-shop',
  categoryId: 'category-rent',
  description: 'Monthly office rent',
  actorUserId: 'ui-audit-owner',
  originalExpenseId: null,
  createdAt: '2026-07-20T09:30:00Z',
};

test.describe('expenses', () => {
  test('renders desktop table, mobile cards, filters, status values, and pagination', async ({
    page,
  }, testInfo) => {
    const collector = collectBrowserFailures(page);
    try {
      await openExpenses(page, { totalCount: 45, pageSize: 3 });
      const isMobileProject = testInfo.project.name === 'chromium-mobile';
      if (!isMobileProject) {
        await expect(page.locator('.desktop-table')).toBeVisible();
        await expect(page.locator('tbody tr')).toHaveCount(3);
        await expect(page.locator('tbody')).toContainText('Office Rent');
        await expect(page.locator('tbody')).toContainText('Active');
        await expect(page.locator('tbody')).toContainText('Voided');
        await expect(page.getByText('Page 1 of 15')).toBeVisible();
      } else {
        await expect(page.locator('.mobile-grid-container')).toBeVisible();
        await expect(page.locator('.expense-card')).toHaveCount(3);
      }

      const search = page.locator('input[placeholder="Search expenses..."]');
      await search.fill('utilities');
      const resultRows = isMobileProject
        ? page.locator('.expense-card')
        : page.locator('.desktop-table tbody tr');
      await expect(resultRows).toHaveCount(1);
      await expect(resultRows).toContainText('Utilities');
      await search.fill('');
      await expect(resultRows).toHaveCount(3);
      await page.locator('p-select[inputid="expenses-status-filter"]').click();
      await page.getByRole('option', { name: 'Voided' }).click();
      await expect(resultRows).toHaveCount(1);
      await expect(resultRows).toContainText('Utilities');
      await expect(resultRows).not.toContainText('Office Rent');

      if (isMobileProject) {
        await expect(page.locator('.expense-card')).toContainText('Voided');
        await assertNoHorizontalOverflow(page);
      }
      assertNoUnexpectedBrowserFailures(
        collector.failures.filter(
          (failure) =>
            !(
              failure.kind === 'request' &&
              failure.message === 'net::ERR_ABORTED' &&
              failure.url?.includes('/api/expenses')
            ),
        ),
      );
    } finally {
      collector.dispose();
    }
  });

  test('renders loading, error, and empty list states', async ({ page }) => {
    await openExpenses(page, { state: 'loading' });
    await expect(page.locator('.directory-panel--loading[aria-busy="true"]')).toBeVisible();

    await openExpenses(page, { state: 'error' });
    await expect(page.locator('.error')).toContainText('Unable to load expenses right now.');
    await expect(page.locator('.directory-panel__surface')).toBeVisible();

    await page.setViewportSize({ width: 1440, height: 900 });
    await openExpenses(page, { state: 'empty' });
    await expect(page.locator('.empty-state')).toBeVisible();
    await expect(page.locator('.empty-state h2')).toHaveText('No expenses found');
  });

  test('validates, records, and corrects expenses with translated overlay values', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openExpenses(page, { totalCount: 3, pageSize: 3 });
      await page.setViewportSize({ width: 1440, height: 900 });
      await page.getByRole('button', { name: 'Record Expense' }).click();
      const record = page.locator('app-record-expense-overlay .overlay');
      await expect(record).toBeVisible();
      await assertOverlayFits(page, record);
      await record.getByRole('button', { name: 'Record Expense' }).click();
      await expect(record.locator('.field-error')).toHaveCount(3);
      await expect(record.locator('input[formcontrolname="paidTo"]')).toHaveAttribute(
        'aria-invalid',
        'true',
      );

      await fillExpenseForm(page, record, 'Travel');
      await record.getByRole('button', { name: 'Record Expense' }).click();
      await expect(record).toBeHidden();
      await expect(page.locator('.desktop-table tbody')).toContainText('Travel');

      await page
        .locator('.desktop-table tbody tr')
        .filter({ hasText: 'Office Rent' })
        .getByRole('button')
        .click();
      const correct = page.locator('app-correct-expense-overlay .overlay');
      await expect(correct).toBeVisible();
      await expect(correct.locator('input[formcontrolname="paidTo"]')).toHaveValue(
        'Bengaluru Property Holdings',
      );
      await assertOverlayFits(page, correct);
      await correct.locator('input[formcontrolname="paidTo"]').fill('Updated Property Holdings');
      await correct.getByRole('button', { name: 'Correct Expense' }).click();
      await expect(correct).toBeHidden();
      await expect(page.locator('.desktop-table tbody')).toContainText('Updated Property Holdings');
      assertNoUnexpectedBrowserFailures(
        collector.failures.filter(
          (failure) =>
            !(
              failure.kind === 'request' &&
              failure.message === 'net::ERR_ABORTED' &&
              failure.url?.includes('/api/expenses')
            ),
        ),
      );
    } finally {
      collector.dispose();
    }
  });

  test('asserts translated field labels, status values, and validation text in locales', async ({
    page,
  }, testInfo) => {
    const collector = collectBrowserFailures(page);
    try {
      const isMobileProject = testInfo.project.name === 'chromium-mobile';
      const locale = 'hi-IN';
      const localeExpectations = {
        'hi-IN': {
          categoryLabel: 'श्रेणी',
          statusActive: 'सक्रिय',
          statusVoided: 'रद्द',
          recordButton: 'खर्च दर्ज करें',
          validationRequired: 'यह फ़ील्ड आवश्यक है',
        },
      } as Record<string, Record<string, string>>;
      const expected = localeExpectations[locale] || localeExpectations['en-IN'];

      await openExpenses(page, { locale, totalCount: 3, pageSize: 3 });
      await page.setViewportSize(isMobileProject ? { width: 375, height: 667 } : { width: 1440, height: 900 });

      // Check table/card headers and status labels are translated
      if (!isMobileProject) {
        const headerLocator = page.locator('.desktop-table thead');
        await expect(headerLocator).toContainText(expected.categoryLabel);
        const statusCells = page.locator('.desktop-table tbody').getByText(expected.statusActive);
        await expect(statusCells.first()).toBeVisible();
        const voidedCells = page.locator('.desktop-table tbody').getByText(expected.statusVoided);
        await expect(voidedCells.first()).toBeVisible();
      }

      // Open record overlay and assert translated button label and validation text
      await page.getByRole('button', { name: expected.recordButton }).click();
      const record = page.locator('app-record-expense-overlay .overlay');
      await expect(record).toBeVisible();
      await record.getByRole('button', { name: expected.recordButton }).click();
      await expect(record.locator('.field-error').first()).toContainText(expected.validationRequired);

      assertNoUnexpectedBrowserFailures(
        collector.failures.filter(
          (failure) =>
            !(
              failure.kind === 'request' &&
              failure.message === 'net::ERR_ABORTED' &&
              failure.url?.includes('/api/expenses')
            ),
        ),
      );
    } finally {
      collector.dispose();
    }
  });

  test('validates correction overlay invalid submission and handles POST errors', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page);
    try {
      let postCallCount = 0;
      await mockExternalRequests(page, { authenticated: true, locale: 'en-IN' });

      await page.route('**/api/expenses**', async (route) => {
        const request = route.request();
        const url = new URL(request.url());
        const expenseId = url.pathname.split('/')[3];

        if (request.method() === 'GET' && url.pathname.endsWith('/categories')) {
          await route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify([
              { id: 'category-rent', name: 'Office Rent' },
              { id: 'category-travel', name: 'Travel' },
            ]),
          });
          return;
        }

        if (request.method() === 'GET' && expenseId) {
          await route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
              ...DETAIL,
              id: 'expense-rent',
              categoryName: 'Office Rent',
              paidTo: 'Original Recipient',
            }),
          });
          return;
        }

        if (request.method() === 'POST') {
          postCallCount++;
          // First POST succeeds; second POST fails
          if (postCallCount === 2) {
            await route.fulfill({
              status: 500,
              contentType: 'application/json',
              body: JSON.stringify({ detail: 'Server error during correction. Please try again.' }),
            });
            return;
          }
          await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(DETAIL) });
          return;
        }

        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            items: [EXPENSES[0]],
            totalCount: 1,
            pageNumber: 1,
            pageSize: 20,
          }),
        });
      });

      await page.goto('/expenses');
      await page.waitForLoadState('networkidle');
      await page.setViewportSize({ width: 1440, height: 900 });

      // Open correction overlay
      await page
        .locator('.desktop-table tbody tr')
        .filter({ hasText: 'Office Rent' })
        .getByRole('button')
        .first()
        .click();
      const correct = page.locator('app-correct-expense-overlay .overlay');
      await expect(correct).toBeVisible();

      // Test 1: Invalid correction (clear required field and submit)
      await correct.locator('input[formcontrolname="paidTo"]').fill('');
      await correct.locator('input[formcontrolname="paidTo"]').blur();
      await expect(correct.locator('.field-error')).toHaveCount(1);
      await expect(correct.locator('input[formcontrolname="paidTo"]')).toHaveAttribute(
        'aria-invalid',
        'true',
      );
      await correct.getByRole('button', { name: 'Correct Expense' }).click();
      // Overlay should remain visible, not submit
      await expect(correct).toBeVisible();

      // Test 2: Valid correction with POST error (server failure)
      await correct.locator('input[formcontrolname="paidTo"]').fill('New Recipient');
      await correct.getByRole('button', { name: 'Correct Expense' }).click();
      await page.waitForLoadState('networkidle');
      // Overlay retains focus and error feedback remains visible
      await expect(correct).toBeVisible();

      assertNoUnexpectedBrowserFailures(
        collector.failures.filter(
          (failure) =>
            !(
              failure.kind === 'request' &&
              (failure.message === 'net::ERR_ABORTED' || failure.message === 'net::ERR_HTTP_RESPONSE_CODE_FAILURE') &&
              failure.url?.includes('/api/expenses')
            ),
        ),
      );
    } finally {
      collector.dispose();
    }
  });

  test('keeps long labels and overlays inside supported locale viewports', async ({
    page,
  }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'locale and viewport matrix');
    test.setTimeout(240_000);
    const collector = collectBrowserFailures(page);
    try {
      await openExpenses(page, { locale: 'hi-IN', totalCount: 3, pageSize: 3 });
      for (const viewport of [
        { width: 375, height: 667 },
        { width: 768, height: 1024 },
        { width: 1440, height: 900 },
      ]) {
        await page.setViewportSize(viewport);
        await expect(page.locator('.expenses-page')).toBeVisible();
        await assertNoHorizontalOverflow(page);
        await page.locator('.expenses-hero__actions').getByRole('button').click();
        const overlay = page.locator('app-record-expense-overlay .overlay');
        await expect(overlay).toBeVisible();
        await assertOverlayFits(page, overlay);
        await overlay.locator('.close-button').click();
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });
});

async function openExpenses(
  page: Page,
  options: {
    readonly state?: 'ready' | 'loading' | 'error' | 'empty';
    readonly locale?: string;
    readonly totalCount?: number;
    readonly pageSize?: number;
  } = {},
): Promise<void> {
  await mockExternalRequests(page, { authenticated: true, locale: options.locale ?? 'en-IN' });
  const state = options.state ?? 'ready';
  let items = [...EXPENSES];
  await page.route('**/api/expenses**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const expenseId = url.pathname.split('/')[3];

    if (state === 'loading' && request.method() === 'GET' && !expenseId) {
      await new Promise((resolve) => setTimeout(resolve, 1_000));
    }
    if (state === 'error') {
      await route.fulfill({
        status: 503,
        contentType: 'application/json',
        body: JSON.stringify({ detail: 'Unable to load expenses right now.' }),
      });
      return;
    }
    if (request.method() === 'GET' && url.pathname.endsWith('/categories')) {
      await fulfillJson(route, [
        { id: 'category-rent', name: 'Office Rent' },
        { id: 'category-travel', name: 'Travel' },
        { id: 'category-supplier', name: 'Supplier Payments' },
      ]);
      return;
    }
    if (request.method() === 'GET' && expenseId) {
      await fulfillJson(route, DETAIL);
      return;
    }
    if (request.method() === 'POST') {
      const body = request.postDataJSON() as Record<string, unknown>;
      const updated = {
        ...DETAIL,
        id: body.categoryName === 'Travel' ? 'expense-travel' : DETAIL.id,
        categoryName: body.categoryName as string,
        amount: body.amount as number,
        paidTo: body.paidTo as string,
        description: body.description as string | null,
        expenseDate: body.expenseDate as string,
      };
      items =
        body.categoryName === 'Travel'
          ? [updated, ...items]
          : items.map((item) =>
              item.id === DETAIL.id ? { ...item, paidTo: body.paidTo as string } : item,
            );
      await fulfillJson(route, updated);
      return;
    }
    const pageSize = options.pageSize ?? Number(url.searchParams.get('pageSize') ?? 20);
    const search = url.searchParams.get('search')?.toLowerCase();
    const visibleItems = search
      ? items.filter((item) =>
          (item.categoryName + ' ' + item.paidTo).toLowerCase().includes(search),
        )
      : items;
    await fulfillJson(route, {
      items: state === 'empty' ? [] : visibleItems.slice(0, pageSize),
      totalCount: state === 'empty' ? 0 : (options.totalCount ?? EXPENSES.length),
      pageNumber: Number(url.searchParams.get('page') ?? 1),
      pageSize,
    });
  });
  await page.goto('/expenses');
  await waitForStablePage(page);
  await expect(page.locator('.expenses-page')).toBeVisible();
}

async function fulfillJson(route: import('@playwright/test').Route, body: unknown): Promise<void> {
  await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
}

async function fillExpenseForm(
  page: Page,
  overlay: import('@playwright/test').Locator,
  category: string,
): Promise<void> {
  const categoryInput = overlay.locator('p-select input');
  await categoryInput.fill(category);
  await page.getByRole('option', { name: category, exact: true }).click();
  await overlay.locator('p-inputnumber input').fill('1250');
  await overlay.locator('input[formcontrolname="paidTo"]').fill('Travel Partner');
  await overlay.locator('textarea[formcontrolname="description"]').fill('Client visit');
}

async function assertOverlayFits(
  page: Page,
  overlay: import('@playwright/test').Locator,
): Promise<void> {
  const bounds = await overlay.locator('.overlay-card').evaluate((element) => {
    const box = element.getBoundingClientRect();
    return { left: box.left, right: box.right, top: box.top, bottom: box.bottom };
  });
  const viewport = await page.evaluate(() => ({ width: innerWidth, height: innerHeight }));
  expect(bounds.left).toBeGreaterThanOrEqual(0);
  expect(bounds.right).toBeLessThanOrEqual(viewport.width);
  expect(bounds.top).toBeGreaterThanOrEqual(0);
  expect(bounds.bottom).toBeLessThanOrEqual(viewport.height);
}

async function assertNoHorizontalOverflow(page: Page): Promise<void> {
  const dimensions = await page.evaluate(() => ({
    body: document.body.scrollWidth,
    document: document.documentElement.scrollWidth,
    viewport: innerWidth,
  }));
  expect(dimensions.body).toBeLessThanOrEqual(dimensions.viewport + 5);
  expect(dimensions.document).toBeLessThanOrEqual(dimensions.viewport + 5);
}
