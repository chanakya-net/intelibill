import { expect, test, type Page, type Route } from '@playwright/test';
import { resolve } from 'node:path';
import { mkdir } from 'node:fs/promises';
import { session, startPurchaseReceivingServer, USER } from './purchase-receiving-server';
import type { TillSession } from '../../../src/app/features/tills/services/till.models';
let server: Awaited<ReturnType<typeof startPurchaseReceivingServer>>;
const OUTPUT = process.env.P0410_EVIDENCE_DIR ?? resolve(__dirname, '../../../.ui-audit/licensing-till-completion');
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

const EXPIRED = 'This shop’s trading access has ended.';
const expiredAuthority = () => ({ code: 'Licence.Expired', enforced: true });
const activeAuthority = () => ({
  schemaVersion: 1,
  shopId: session().activeShopId,
  enforced: true,
  authorityVerifiedAt: new Date().toISOString(),
  validUntil: new Date(Date.now() + 60000).toISOString(),
});
const closeSession = (value: TillSession, countedCash: number) => {
  const variance = countedCash - value.expectedCash;
  value.state = 'Closed';
  value.version += 1;
  value.awaitingReview = variance !== 0;
  value.close = {
    id: 'real-close',
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
    expectedCash: value.expectedCash,
    countedCash,
    variance,
    closedBy: USER,
    closedAt: new Date().toISOString(),
    reason: null,
    overrideReason: null,
    isOverdue: false,
    awaitingReview: variance !== 0,
    businessDate: value.businessDate,
    tillName: value.tillName,
    cashierName: value.cashierName,
    cashierId: USER,
  } as TillSession['close'];
};

test('expired access preserves old draft and permits counted close', async ({ page }) => {
  const { value } = await fixture(page);
  const opens: { key: string; body: Record<string, unknown> }[] = [];
  const closes: Record<string, unknown>[] = [];
  let openMode: 'unknown' | 'expired' | 'ok' = 'unknown';
  await page.route('**/api/licensing/shops/*/trading-authority', (route) =>
    json(route, activeAuthority()),
  );
  await page.route('**/api/tills/till-one/sessions', (route) => {
    if (route.request().method() !== 'POST') return route.fallback();
    const body = route.request().postDataJSON();
    opens.push({ key: body.operationKey, body });
    if (openMode === 'unknown') return route.abort('failed');
    return openMode === 'expired' ? json(route, { code: 'Licence.Expired' }, 403) : json(route, value);
  });
  await page.route('**/api/tills/sessions/session-one/close', (route) => {
    closes.push(route.request().postDataJSON());
    closeSession(value, 1300);
    return json(route, value);
  });
  await page.goto(`${server.origin}/tills`);
  await page.getByRole('combobox', { name: 'Physical till', exact: true }).selectOption('till-one');
  await page.getByLabel('Counted opening float', { exact: true }).fill('1000');
  await page.getByRole('button', { name: 'Open session', exact: true }).click();
  await expect(page.getByText('An earlier request is unresolved.', { exact: false })).toBeVisible();
  // The page stays mounted: expiry is first learned through the frozen retry.
  await expect(page.getByText(EXPIRED)).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Open session', exact: true })).toBeEnabled();
  openMode = 'expired';
  await page.getByRole('button', { name: 'Recover / retry frozen request' }).click();
  await expect(page.getByText(EXPIRED)).toHaveCount(2); // shared access banner + retry alert
  await expect(page.getByRole('button', { name: 'Open session', exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Recover / retry frozen request' })).toBeVisible();
  openMode = 'ok';
  await page.getByRole('button', { name: 'Recover / retry frozen request' }).click();
  await expect(page.getByText('The original frozen request was confirmed', { exact: false }).first()).toBeVisible();
  await expect(page.getByText(EXPIRED)).toHaveCount(1); // retry alert cleared, banner stays
  await expect(page.getByRole('button', { name: 'Open session', exact: true })).toBeDisabled();
  expect(opens).toHaveLength(3);
  expect(new Set(opens.map((x) => x.key)).size).toBe(1);
  expect(opens[1].body).toEqual(opens[0].body);
  expect(opens[2].body).toEqual(opens[0].body);
  await inspect(page);
  await page.getByLabel('Cash actually counted', { exact: true }).fill('1300');
  await page.getByRole('button', { name: 'Count & close', exact: true }).click();
  await expect.poll(() => closes.length).toBe(1);
  expect(closes[0]).toMatchObject({ countedCash: 1300, expectedVersion: 7 });
  await expect(page.locator('.close-summary')).toContainText('₹1,300.00');
  await expect(page.locator('.close-summary')).toContainText('Closed');
  await page.screenshot({ path: `${OUTPUT}/expired-till-completion.png`, fullPage: true });
});

test('manager can review a frozen close after expiry', async ({ page }) => {
  const { value } = await fixture(page, 'closed');
  const reviews: Record<string, unknown>[] = [];
  await page.route('**/api/licensing/shops/*/trading-authority', (route) =>
    json(route, expiredAuthority(), 403),
  );
  await page.route('**/api/tills/sessions/session-one/review', (route) => {
    reviews.push(route.request().postDataJSON());
    value.version += 1;
    value.awaitingReview = false;
    value.review = {
      id: 'real-review',
      closeId: 'frozen-close',
      reviewedBy: USER,
      reviewedAt: new Date().toISOString(),
      reason: 'Count checked',
    };
    return json(route, value);
  });
  await page.goto(`${server.origin}/tills`);
  await expect(page.getByText(EXPIRED).first()).toBeVisible();
  await inspect(page);
  await expect(page.locator('.review-confirmed')).toHaveCount(0);
  await page.getByLabel('Manager review reason', { exact: true }).fill('Count checked');
  await page.getByRole('button', { name: 'Acknowledge frozen variance' }).click();
  await expect.poll(() => reviews.length).toBe(1);
  expect(reviews[0]).toMatchObject({ reason: 'Count checked', expectedVersion: 7 });
  await expect(page.locator('.review-confirmed')).toContainText('Count checked');
  await expect(page.getByRole('button', { name: 'Acknowledge frozen variance' })).toHaveCount(0);
  await page.screenshot({ path: `${OUTPUT}/expired-till-review.png`, fullPage: true });
});
