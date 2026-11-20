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

test('expired access preserves old draft and permits counted close', async ({ page }) => {
  await fixture(page);
  let expired = false;
  const posts: string[] = [];
  await page.route('**/api/licensing/shops/*/trading-authority', route => expired
    ? json(route, { code: 'Licence.Expired', enforced: true }, 403)
    : json(route, { schemaVersion: 1, shopId: session().activeShopId, enforced: true,
      authorityVerifiedAt: new Date().toISOString(), validUntil: new Date(Date.now() + 60000).toISOString() }));
  await page.route('**/api/tills/till-one/sessions', route => {
    if (route.request().method() !== 'POST') return route.fallback();
    posts.push('open');
    return expired ? json(route, { code: 'Licence.Expired' }, 403) : route.abort('failed');
  });
  await page.route('**/api/tills/sessions/session-one/close', route => {
    posts.push('close');
    return json(route, openSession());
  });
  await page.goto(`${server.origin}/tills`);
  await page.getByRole('combobox', { name: 'Physical till', exact: true }).selectOption('till-one');
  await page.getByLabel('Counted opening float', { exact: true }).fill('1000');
  await page.getByRole('button', { name: 'Open session', exact: true }).click();
  await expect(page.getByText('An earlier request is unresolved.', { exact: false })).toBeVisible();
  expired = true;
  await page.reload();
  await expect(page.getByText('This shop’s trading access has ended.').first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'Open session', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Recover / retry frozen request' }).click();
  await expect(page.getByText('This shop’s trading access has ended.').last()).toBeVisible();
  await inspect(page);
  await page.getByLabel('Cash actually counted', { exact: true }).fill('1300');
  await page.getByRole('button', { name: 'Count & close', exact: true }).click();
  await expect.poll(() => posts.filter(x => x === 'close').length).toBe(1);
  expect(posts.filter(x => x === 'open')).toHaveLength(2);
  await page.screenshot({ path: `${OUTPUT}/expired-till-completion.png`, fullPage: true });
});

test('manager can review a frozen close after expiry', async ({ page }) => {
  const { posts } = await fixture(page, 'closed');
  await page.route('**/api/licensing/shops/*/trading-authority', route =>
    json(route, { code: 'Licence.Expired', enforced: true }, 403));
  await page.goto(`${server.origin}/tills`);
  await expect(page.getByText('This shop’s trading access has ended.').first()).toBeVisible();
  await inspect(page);
  await page.getByLabel('Manager review reason', { exact: true }).fill('Count checked');
  await page.getByRole('button', { name: 'Acknowledge frozen variance' }).click();
  await expect.poll(() => posts.length).toBe(1);
  await page.screenshot({ path: `${OUTPUT}/expired-till-review.png`, fullPage: true });
});
