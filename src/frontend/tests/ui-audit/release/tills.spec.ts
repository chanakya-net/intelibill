import { expect, test, type Page, type Route } from '@playwright/test';
import { resolve } from 'node:path';
import { mkdir } from 'node:fs/promises';
import { session, startPurchaseReceivingServer, USER } from './purchase-receiving-server';
import type { TillSession } from '../../../src/app/features/tills/services/till.models';
let server: Awaited<ReturnType<typeof startPurchaseReceivingServer>>;
const OUTPUT = resolve(__dirname, '../../../.ui-audit/till-task7');
const json = (route: Route, value: unknown, status = 200) =>
  route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) });
const today = () =>
  new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Kolkata',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());
const openSession = (): TillSession => ({
  id: 'session-one',
  tillId: 'till-one',
  cashierId: USER,
  tillName: 'Front counter',
  cashierName: 'Asha Rao',
  businessDate: today(),
  businessTimeZoneId: 'Asia/Kolkata',
  openedAt: new Date().toISOString(),
  state: 'Open',
  version: 7,
  openingFloat: 1000,
  expectedCash: 1300,
  recordedTenderTotals: { Cash: 300, Upi: 700 },
  storeCreditIssued: 0,
  storeCreditRedeemed: 20,
  receivableChange: 100,
  lastMovementSequence: 2,
  close: null,
  review: null,
  latePostings: [],
  awaitingReview: false,
});
const movements = () => ({
  items: [
    {
      id: 'movement-one',
      sequence: 1,
      sourceType: 'SaleReturn',
      sourceId: 'real-return-id',
      parentSaleId: 'real-parent-sale-id',
      purpose: 'sale-return',
      method: 'Cash',
      signedAmount: -200,
      occurredAt: new Date().toISOString(),
      recordedAt: new Date().toISOString(),
      actorUserId: USER,
      operationKey: 'original-return-operation',
      reason: 'Damaged packaging',
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 1,
});
test.beforeEach(async ({ page }) => {
  server = await startPurchaseReceivingServer(
    resolve(__dirname, '../../../dist/INVENTORY/browser'),
  );
  await page.addInitScript(
    (value) => localStorage.setItem('inventory.auth.session.local', JSON.stringify(value)),
    session(),
  );
  await mkdir(OUTPUT, { recursive: true });
});
test.afterEach(async () => server.close());
async function fixture(
  page: Page,
  state: 'normal' | 'empty' | 'error' | 'overdue' | 'closed' | 'unknown' = 'normal',
) {
  const value = openSession();
  if (state === 'overdue') value.businessDate = '2026-11-07';
  if (state === 'closed') {
    value.state = 'Closed';
    value.close = {
      id: 'frozen-close',
      openingFloat: 1000,
      lastMovementSequence: 2,
      totals: {
        recordedTenderTotals: { Cash: 300, Upi: 700 },
        purposeTotals: { sale: 1000 },
        storeCreditIssued: 0,
        storeCreditRedeemed: 20,
        receivableChange: 100,
        movementCount: 2,
      },
      expectedCash: 1300,
      countedCash: 1290,
      variance: -10,
      closedBy: USER,
      closedAt: new Date().toISOString(),
      reason: 'Counted twice',
      overrideReason: null,
      isOverdue: false,
      awaitingReview: true,
      businessDate: today(),
      tillName: 'Frozen front counter',
      cashierName: 'Frozen Asha Rao',
      cashierId: USER,
    };
    value.awaitingReview = true;
    value.latePostings = [
      {
        id: 'late',
        offlineSubmissionId: 'submission',
        saleId: 'late-sale',
        caseId: 'late-case',
        sessionId: value.id,
        closeId: 'frozen-close',
        sourceBusinessDate: today(),
        occurredAt: new Date().toISOString(),
        recordedAt: new Date().toISOString(),
        cashierId: USER,
        tenderLines: [],
        evidenceStatus: 'Unknown',
        managerReason: 'Reviewed legacy envelope',
        includedInCount: false,
        supplementalAmount: null,
      },
    ];
  }
  const posts: unknown[] = [];
  await page.route('**/api/users', (r) =>
    json(r, [{ userId: USER, firstName: 'Asha', lastName: 'Rao', status: 'Active' }]),
  );
  await page.route(/\/api\/tills(?:\/|\?|$)/, async (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/capabilities'))
      return state === 'error'
        ? json(route, { code: 'Unavailable' }, 503)
        : json(route, { enabled: true, mixedTenderEnabled: false, businessDate: today() });
    if (route.request().method() === 'POST') {
      posts.push(route.request().postDataJSON());
      return state === 'unknown' ? route.abort('failed') : json(route, value);
    }
    if (path.includes('/operations/')) return json(route, { code: 'Operation.NotFound' }, 404);
    if (path === '/api/tills') return json(route, [{ id: 'till-one', name: 'Front counter' }]);
    if (path === '/api/tills/sessions')
      return json(route, {
        items: state === 'empty' ? [] : [value],
        page: 1,
        pageSize: 20,
        totalCount: state === 'empty' ? 0 : 1,
      });
    return json(route, {
      session: value,
      movements: movements(),
      sources: { items: [], page: 1, pageSize: 20, totalCount: 0 },
    });
  });
  return { value, posts };
}
async function inspect(page: Page) {
  await page
    .getByRole('button')
    .filter({ has: page.locator('strong', { hasText: 'Front counter' }) })
    .click();
  await expect(page.getByRole('heading', { name: 'Immutable source audit' })).toBeVisible();
}
for (const width of [1440, 390])
  test(`normal till audit and count at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await fixture(page);
    await page.goto(`${server.origin}/tills`);
    await expect(
      page.getByRole('heading', { name: 'Tills & day close', exact: true }),
    ).toBeVisible();
    await inspect(page);
    await expect(
      page.getByRole('link', { name: 'Open authorized source reference' }),
    ).toHaveAttribute('href', '/sales/real-parent-sale-id/print');
    await expect(page.getByText('Recorded UPI:', { exact: false })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
      true,
    );
    await page.evaluate(() => scrollTo(0, 0));
    await page.screenshot({ path: `${OUTPUT}/normal-${width}.png`, fullPage: true });
  });
for (const state of ['empty', 'error', 'overdue', 'closed'] as const)
  test(`renders ${state} state`, async ({ page }) => {
    await fixture(page, state);
    await page.goto(`${server.origin}/tills`);
    if (state === 'empty') await expect(page.getByText('No sessions to show.')).toBeVisible();
    else if (state === 'error') await expect(page.getByRole('alert').first()).toBeVisible();
    else {
      await inspect(page);
      if (state === 'overdue') {
        await expect(page.getByText('This is an overdue count', { exact: false })).toBeVisible();
        await expect(page.getByRole('button', { name: 'Cash in', exact: true })).toHaveCount(0);
      } else {
        await expect(
          page.getByText('Awaiting manager review', { exact: false }).first(),
        ).toBeVisible();
        await page.getByRole('link', { name: 'Print frozen close' }).click();
        await expect(page.getByRole('heading', { name: 'Frozen front counter' })).toBeVisible();
        await expect(page.getByText('Amount unknown', { exact: true })).toBeVisible();
      }
    }
    await page.evaluate(() => scrollTo(0, 0));
    await page.screenshot({ path: `${OUTPUT}/${state}.png`, fullPage: true });
  });
test('unknown movement shows original business facts and reloads the exact request', async ({
  page,
}) => {
  const { posts } = await fixture(page, 'unknown');
  await page.goto(`${server.origin}/tills`);
  await inspect(page);
  await page.getByLabel('Amount', { exact: true }).fill('200');
  await page
    .getByLabel('Reason (required for transfers, variance or overdue finalization)', {
      exact: true,
    })
    .fill('Float transfer from safe');
  await page.getByRole('button', { name: 'Cash in', exact: true }).click();
  await expect(page.getByText('An earlier request is unresolved.', { exact: false })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Cash in', exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Cash in', exact: true })).toBeVisible();
  await page.evaluate(() => scrollTo(0, 0));
  await page.screenshot({ path: `${OUTPUT}/unknown.png`, fullPage: true });
  expect(posts).toHaveLength(1);
});

test('Staff opens and counts; manager reviews and prints the frozen close', async ({
  page,
  browser,
}) => {
  const staff = session();
  staff.shops = staff.shops.map((x) => ({ ...x, role: 'Staff' }));
  await page.addInitScript(
    (value) => localStorage.setItem('inventory.auth.session.local', JSON.stringify(value)),
    staff,
  );
  let current: TillSession | null = null;
  const posts: { path: string; body: Record<string, unknown> }[] = [];
  async function install(view: Page) {
    await view.route('**/api/users', (r) =>
      json(r, [{ userId: USER, firstName: 'Asha', lastName: 'Rao', status: 'Active' }]),
    );
    await view.route(/\/api\/tills(?:\/|\?|$)/, async (route) => {
      const path = new URL(route.request().url()).pathname;
      if (path.endsWith('/capabilities'))
        return json(route, { enabled: true, mixedTenderEnabled: false, businessDate: today() });
      if (path === '/api/tills') return json(route, [{ id: 'till-one', name: 'Front counter' }]);
      if (route.request().method() === 'POST') {
        const body = route.request().postDataJSON();
        posts.push({ path, body });
        if (path === '/api/tills/till-one/sessions')
          current = {
            ...openSession(),
            openingFloat: body.openingFloat,
            expectedCash: body.openingFloat,
            recordedTenderTotals: {},
            lastMovementSequence: 0,
          };
        else if (path.endsWith('/cash-movements'))
          current = {
            ...current!,
            version: 8,
            expectedCash: 1020,
            lastMovementSequence: 1,
            recordedTenderTotals: { Cash: 20 },
          };
        else if (path.endsWith('/close'))
          current = {
            ...current!,
            state: 'Closed',
            version: 9,
            awaitingReview: true,
            close: {
              id: 'journey-close',
              openingFloat: 1000,
              lastMovementSequence: 1,
              totals: {
                recordedTenderTotals: { Cash: 20 },
                purposeTotals: { 'cash-in': 20 },
                storeCreditIssued: 0,
                storeCreditRedeemed: 0,
                receivableChange: 0,
                movementCount: 1,
              },
              expectedCash: 1020,
              countedCash: body.countedCash,
              variance: body.countedCash - 1020,
              closedBy: USER,
              closedAt: new Date().toISOString(),
              reason: body.reason,
              overrideReason: null,
              isOverdue: false,
              awaitingReview: true,
              businessDate: today(),
              tillName: 'Front counter',
              cashierName: 'Asha Rao',
              cashierId: USER,
            },
          };
        else if (path.endsWith('/review'))
          current = {
            ...current!,
            awaitingReview: false,
            review: {
              id: 'review',
              closeId: 'journey-close',
              reviewedBy: 'manager-user',
              reviewedAt: new Date().toISOString(),
              reason: body.reason,
            },
          };
        return json(route, current);
      }
      if (path === '/api/tills/sessions')
        return json(route, {
          items: current ? [current] : [],
          page: 1,
          pageSize: 20,
          totalCount: current ? 1 : 0,
        });
      return json(route, {
        session: current,
        movements: { items: [], page: 1, pageSize: 20, totalCount: 0 },
        sources: { items: [], page: 1, pageSize: 20, totalCount: 0 },
      });
    });
  }
  await install(page);
  await page.goto(`${server.origin}/tills`);
  await expect(page.getByRole('heading', { name: 'Create named till' })).toHaveCount(0);
  await page.getByRole('combobox', { name: 'Physical till', exact: true }).selectOption('till-one');
  await page.getByLabel('Counted opening float', { exact: true }).fill('1000');
  await page.getByRole('button', { name: 'Open session', exact: true }).click();
  await inspect(page);
  await page.getByLabel('Amount', { exact: true }).fill('20');
  await page
    .getByLabel('Reason (required for transfers, variance or overdue finalization)', {
      exact: true,
    })
    .fill('Float from safe');
  await page.getByRole('button', { name: 'Cash in', exact: true }).click();
  await expect(page.getByLabel('Amount', { exact: true })).toHaveValue('0');
  await page.getByLabel('Cash actually counted', { exact: true }).fill('1010');
  await page
    .getByLabel('Reason (required for transfers, variance or overdue finalization)', {
      exact: true,
    })
    .fill('Count checked twice');
  await page.getByRole('button', { name: 'Count & close', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Print frozen close' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Acknowledge frozen variance' })).toHaveCount(0);
  const context = await browser.newContext();
  const manager = session();
  manager.user.id = 'manager-user';
  await context.addInitScript(
    (value) => localStorage.setItem('inventory.auth.session.local', JSON.stringify(value)),
    manager,
  );
  const review = await context.newPage();
  await install(review);
  await review.goto(`${server.origin}/tills`);
  await inspect(review);
  await review
    .getByLabel('Manager review reason', { exact: true })
    .fill('Witnessed recount accepted');
  await review.getByRole('button', { name: 'Acknowledge frozen variance', exact: true }).click();
  await expect(review.locator('p').filter({ hasText: 'Witnessed recount accepted' })).toBeVisible();
  await review.getByRole('link', { name: 'Print frozen close' }).click();
  await expect(review.getByText('₹1,010.00', { exact: true })).toBeVisible();
  await expect(review.getByText('-₹10.00', { exact: true })).toBeVisible();
  expect(posts.map((x) => x.path.split('/').pop())).toEqual([
    'sessions',
    'cash-movements',
    'close',
    'review',
  ]);
  expect(posts.every((x) => typeof x.body.operationKey === 'string')).toBe(true);
  expect(posts[2].body).toMatchObject({
    countedCash: 1010,
    expectedVersion: 8,
    reason: 'Count checked twice',
  });
  expect(current?.close?.countedCash).toBe(1010);
  expect(current?.close?.variance).toBe(-10);
  await context.close();
});
