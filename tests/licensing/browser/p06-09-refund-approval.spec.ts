import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type BrowserContext, type Page } from '@playwright/test';
import { fillSecret, otp } from './p01-12-fixture';

function required(name: string): string {
  const value = process.env['P0609_' + name];
  if (!value) throw new Error('Missing refund-approval fixture variable ' + name);
  return value;
}

const merchant = required('MERCHANT_WEB');
const admin = required('ADMIN_WEB');
const scratch = required('TEMPORARY');
const evidence = required('EVIDENCE_DIR');
const account = required('ACCOUNT');
const request = required('REQUEST');
const laterRequest = required('LATER_REQUEST');
const reason = 'Duplicate branch purchased before go-live';
const approvals = `/api/admin/accounts/${account}/refund-requests/${request}/approvals`;
const approvePage = (id: string) => `${admin}/payments/refunds/${account}/${id}/approve`;
const secretsOf = (file: string) => JSON.parse(readFileSync(join(scratch, file), 'utf8'));
const options = { viewport: { width: 1440, height: 900 }, locale: 'en-IN', timezoneId: 'Asia/Kolkata' } as const;

async function checkViewports(page: Page, name: string) {
  for (const [label, width, height] of [['desktop', 1440, 900], ['mobile', 390, 844]] as const) {
    await page.setViewportSize({ width, height });
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    // The console sidebar slides off-canvas on narrow screens: capture only once that transition has finished.
    const sidebar = page.locator('.adm-sidebar');
    if (width < 800 && await sidebar.count())
      await expect.poll(() => sidebar.evaluate(element => element.getBoundingClientRect().right <= 1)).toBe(true);
    await page.screenshot({ path: join(evidence, `${name}-${label}.png`), fullPage: true });
  }
  await page.setViewportSize(options.viewport);
}

async function submitSignIn(page: Page, secrets: Record<string, string>) {
  await page.getByLabel('Email', { exact: true }).fill(secrets['admin']);
  await fillSecret(page, 'Password', secrets['adminPassword']);
  await fillSecret(page, 'Authenticator code', otp(secrets['seed'], Math.floor(Number(secrets['adminTime']) / 30)));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
}

async function fillApproval(page: Page, rupees: string, treatment: string, effectiveIst?: string) {
  await expect(page.getByTestId('refund-approval-form')).toBeVisible();
  await page.locator('#refund-approval-amount').fill(rupees);
  await page.locator('#refund-approval-reason').fill(reason);
  await page.locator(`input[name="treatment"][value="${treatment}"]`).check();
  if (effectiveIst) await page.locator('#refund-approval-effective').fill(effectiveIst);
}

async function preview(page: Page, id: string) {
  const answered = page.waitForResponse(r => r.request().method() === 'POST'
    && new URL(r.url()).pathname.endsWith(`/refund-requests/${id}/approval-preview`));
  await page.getByTestId('refund-approval-preview').click();
  return answered;
}

async function merchantSeesPending(context: BrowserContext, secrets: Record<string, string>) {
  const payer = await context.newPage();
  await payer.goto(merchant + '/login');
  await payer.locator('#identifier').fill(secrets['merchant']);
  await payer.locator('#password').evaluate((input: HTMLInputElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, secrets['merchantPassword']);
  await payer.locator('button[type=submit]').click();
  await expect(payer).not.toHaveURL(/\/login/);
  const listed = payer.waitForResponse(r => r.request().method() === 'GET'
    && r.url().includes(`/api/billing/accounts/${account}/refund-requests`));
  await payer.goto(`${merchant}/billing/accounts/${account}/refunds`);
  const page = await (await listed).json();
  const row = payer.locator(`[data-testid="refund-request-row"][data-refund-id="${request}"]`);
  await expect(row).toHaveAttribute('data-status', 'Pending');
  await expect(row.getByTestId('refund-request-status')).toHaveText('Pending provider');
  await expect(row).toContainText('60.00');
  await checkViewports(payer, 'merchant-refund-pending');
  return page.items.find((item: { refundRequestId: string }) => item.refundRequestId === request);
}

/** Preserve for ₹45.00 of the ₹60.00 request: exact figures from the real API and the named access impact. */
async function reviewPreserve(operator: Page) {
  // Nothing can be previewed until the operator names an amount, a reason and one treatment.
  await expect(operator.getByTestId('refund-approval-preview')).toBeDisabled();
  await fillApproval(operator, '45.00', 'Preserve');
  const previewed = await preview(operator, request);
  expect(previewed.status()).toBe(200);
  expect(previewed.headers()['cache-control']).toContain('no-store');
  const figures = await previewed.json();
  expect([figures.verifiedAmountPaise, figures.requestedAmountPaise, figures.remainingAmountPaise,
    figures.proposedAmountPaise, figures.proposedRemainingAmountPaise]).toEqual([12000, 6000, 12000, 4500, 7500]);
  const impact = operator.getByTestId('refund-approval-impact');
  for (const text of ['₹120.00', '₹60.00', '₹45.00', '₹75.00', 'Refund Central', 'Refund East', 'Access is preserved'])
    await expect(impact).toContainText(text);
  await checkViewports(operator, 'admin-approval-preview');
  await operator.locator('#refund-approval-confirm').check();
}

/** A second tab after reload sees the same frozen intent and recovers the committed receipt by its original key. */
async function recoverInSecondTab(context: BrowserContext, key: string, saved: Record<string, unknown>) {
  const second = await context.newPage();
  await second.goto(admin);
  await second.getByRole('link', { name: 'Refund requests', exact: true }).click();
  await second.getByTestId('refund-intake-account').fill(account);
  await second.getByTestId('refund-intake-search').click();
  await second.locator(`[data-refund-id="${request}"]`).getByTestId('refund-intake-view').click();
  await expect(second.getByTestId('refund-approval-operation')).toHaveText(key);
  await expect(second.getByTestId('refund-approval-form')).toHaveCount(0);
  const lookup = second.waitForResponse(r => r.request().method() === 'GET' && r.url().endsWith(`${approvals}/operations/${key}`));
  await second.getByTestId('refund-approval-check').click();
  const recovered = await lookup;
  expect(recovered.status()).toBe(200);
  expect(recovered.headers()['cache-control']).toContain('no-store');
  expect(await recovered.json()).toEqual(saved);
  const receipt = second.getByTestId('refund-approval-receipt');
  for (const text of ['₹45.00', '₹75.00', String(saved['reservationId']), String(saved['accessDecisionId'])])
    await expect(receipt).toContainText(text);
  await expect(second.getByTestId('refund-approval-status')).toHaveText('Pending provider');
  await checkViewports(second, 'admin-approval-receipt');
  return second;
}

async function dropCommittedResponse(operator: Page) {
  // The real API commits the approval; only its answer is lost on the way back to Chromium.
  let committed: { status: number; cacheControl: string; body: Record<string, unknown> } | null = null;
  await operator.route('**' + approvals, async route => {
    if (route.request().method() !== 'POST' || committed) return route.continue();
    const response = await route.fetch();
    committed = { status: response.status(), cacheControl: response.headers()['cache-control'], body: await response.json() };
    await route.abort('connectionreset');
  });
  await operator.getByTestId('refund-approval-submit').click();
  // The unknown-outcome message is the settled state; the pending panel alone also shows while the request is in flight.
  await expect(operator.getByTestId('refund-approval-error')).toContainText('outcome is unknown');
  await expect(operator.getByTestId('refund-approval-pending')).toBeVisible();
  expect(committed!.status).toBe(201);
  expect(committed!.cacheControl).toContain('no-store');
  const key = (await operator.getByTestId('refund-approval-operation').textContent())!.trim();
  expect(committed!.body['operationKey']).toBe(key);
  await checkViewports(operator, 'admin-approval-unknown');
  return { key, committed: committed! };
}

test('@approve finance previews, approves, loses the response and recovers the approval', async ({ browser }) => {
  const secrets = secretsOf('sessions.json');
  const context = await browser.newContext(options);
  const operator = await context.newPage();
  await operator.goto(admin + '/auth/sign-in');
  await submitSignIn(operator, secrets);
  await operator.getByRole('link', { name: 'Refund requests', exact: true }).click();
  await operator.getByTestId('refund-intake-account').fill(account);
  await operator.getByTestId('refund-intake-search').click();
  const row = operator.locator(`[data-testid="refund-intake-row"][data-refund-id="${request}"]`);
  await expect(row.getByTestId('refund-intake-status')).toHaveText('Requested');
  await row.getByTestId('refund-intake-approve').click();
  await expect(operator).toHaveURL(approvePage(request));

  await reviewPreserve(operator);

  const { key, committed } = await dropCommittedResponse(operator);

  const savedDevice = await context.storageState({ indexedDB: true });
  const other = await browser.newContext({ ...options, storageState: { cookies: [],
    origins: savedDevice.origins.map(origin => ({ ...origin, localStorage: [] })) } });
  const colleague = await other.newPage();
  await colleague.goto(admin + '/auth/sign-in');
  await submitSignIn(colleague, secretsOf('second.json'));
  await expect(colleague).not.toHaveURL(/sign-in/);
  await colleague.goto(approvePage(request));
  await expect(colleague.getByTestId('refund-approval-decided')).toBeVisible();
  await expect(colleague.getByTestId('refund-approval-operation')).toHaveCount(0);
  await other.close();
  await operator.close();
  const returned = await recoverInSecondTab(context, key, committed.body);

  // Reloading after recovery shows the authoritative decision; the intake shows the decided request and offers no second approval.
  await returned.reload();
  await expect(returned.getByTestId('refund-approval-decided')).toBeVisible();
  await returned.goto(`${admin}/payments/refunds?account=${account}`);
  const decidedRow = returned.locator(`[data-refund-id="${request}"]`);
  await expect(decidedRow.getByTestId('refund-intake-status')).toHaveText('Pending provider');
  await expect(decidedRow.getByTestId('refund-intake-approve')).toHaveCount(0);
  await expect(returned.locator(`[data-refund-id="${laterRequest}"]`).getByTestId('refund-intake-approve')).toBeVisible();
  await checkViewports(returned, 'admin-intake-pending');
  await context.storageState({ path: join(scratch, 'admin-state.json') });

  const customer = await merchantSeesPending(await browser.newContext(options), secrets);
  writeFileSync(join(evidence, 'browser-observations.json'), JSON.stringify({
    refundRequestId: request, operationKey: key, previewStatus: 200, approvalStatus: committed.status,
    approvalResponseDroppedInTransit: true, recoveryStatus: 200, recoveredReceiptEqualsCommitted: true,
    receipt: { approvalId: committed.body['approvalId'], reservationId: committed.body['reservationId'],
      accessDecisionId: committed.body['accessDecisionId'], approvedAmountPaise: committed.body['approvedAmountPaise'],
      remainingAfterPaise: committed.body['remainingAfterPaise'], adminId: committed.body['adminId'],
      treatment: (committed.body['access'] as Record<string, unknown>)['treatment'] },
    figures: { verified: 12000, requested: 6000, approved: 4500, remaining: 7500 },
    merchant: { status: customer.status, amountPaise: customer.amountPaise },
    boundary: 'Development simulator receipt fixture and shipping local builds; real local APIs and PostgreSQL; no provider dispatch.',
  }, null, 2));
});

test('@mfa five-minute-old MFA must sign in again before previewing a named access effect', async ({ browser }) => {
  const context = await browser.newContext({ ...options, storageState: join(scratch, 'admin-state.json') });
  const operator = await context.newPage();
  await operator.goto(approvePage(laterRequest));
  await fillApproval(operator, '15.00', 'EndAt', '2028-02-10T10:00');
  const refused = await preview(operator, laterRequest);
  expect(refused.status()).toBe(403);
  expect((await refused.json()).code).toBe('ManualPayment.RecentMfaRequired');
  const signIn = operator.getByTestId('refund-approval-sign-in');
  await expect(signIn).toBeVisible();
  await checkViewports(operator, 'admin-approval-mfa-required');

  // Signing in again with a fresh authenticator code returns to this exact request.
  await signIn.click();
  await submitSignIn(operator, secretsOf('sessions-mfa.json'));
  await expect(operator).toHaveURL(approvePage(laterRequest));
  await fillApproval(operator, '15.00', 'EndAt', '2028-02-10T10:00');
  const ended = await preview(operator, laterRequest);
  expect(ended.status()).toBe(200);
  const impact = operator.getByTestId('refund-approval-impact');
  for (const text of ['Access ends', 'Refund Central', 'Refund East', '10 Feb 2028, 10:00:00 IST', '₹15.00', '₹60.00'])
    await expect(impact).toContainText(text);
  await checkViewports(operator, 'admin-approval-preview-end-at');

  await operator.locator('input[name="treatment"][value="TermAdjustment"]').check();
  await expect(impact).toHaveCount(0);
  const adjusted = await preview(operator, laterRequest);
  expect(adjusted.status()).toBe(200);
  const access = (await adjusted.json()).access;
  for (const text of ['Paid-through moves', '10 Feb 2028, 10:00:00 IST', '17 Feb 2028, 10:00:00 IST'])
    await expect(impact).toContainText(text);
  await checkViewports(operator, 'admin-approval-preview-term-adjustment');
  writeFileSync(join(evidence, 'browser-observations-mfa.json'), JSON.stringify({
    refundRequestId: laterRequest, staleMfaPreviewStatus: 403, staleMfaCode: 'ManualPayment.RecentMfaRequired',
    returnedToRequestAfterSignIn: true, endAtPreviewStatus: 200, termAdjustmentPreviewStatus: 200,
    termAdjustment: { effectiveAt: access.effectiveAt, previousPaidThrough: access.previousPaidThrough,
      newPaidThrough: access.newPaidThrough, newAccessEndsAt: access.newAccessEndsAt,
      shops: access.shops.map((shop: { shopName: string }) => shop.shopName) },
    approvalsSent: 0,
  }, null, 2));
  await context.storageState({ path: join(scratch, 'fresh-admin.json') });
});

async function reserve(page: Page, id: string, rupees: string) {
  await page.goto(approvePage(id));
  await fillApproval(page, rupees, 'Preserve');
  expect((await preview(page, id)).status()).toBe(200);
  await page.locator('#refund-approval-confirm').check();
  await page.getByTestId('refund-approval-submit').click();
  await expect(page.getByTestId('refund-approval-receipt')).toBeVisible();
  await expect(page.getByTestId('refund-approval-status')).toHaveText('Pending provider');
}

test('@ledger shows depleted balances and opens a request beyond the first thousand', async ({ browser }) => {
  const context = await browser.newContext({ ...options, storageState: join(scratch, 'fresh-admin.json') });
  const page = await context.newPage();
  const id = required('EXCESS');
  await reserve(page, required('FIRST_HOLD'), '35.00');
  await page.goto(approvePage(id));
  for (const remaining of ['₹40.00', '₹0.00']) {
    await fillApproval(page, '70.00', 'Preserve');
    expect((await preview(page, id)).status()).toBe(409);
    await expect(page.getByRole('alert')).toContainText(remaining);
    await expect(page.getByTestId('refund-approval-submit')).toHaveCount(0);
    if (remaining === '₹0.00') break;
    await reserve(page, required('LAST_HOLD'), '40.00');
    await page.goto(`${admin}/payments/refunds?account=${account}`);
    for (let index = 1; index <= 40; index++) {
      await expect(page.getByTestId('refund-intake-row')).toHaveCount(index * 25);
      await page.getByTestId('refund-intake-more').click();
    }
    await page.locator(`[data-refund-id="${id}"]`).getByTestId('refund-intake-approve').click();
    await expect(page).toHaveURL(approvePage(id));
  }
  await checkViewports(page, 'admin-exhausted-receipt');
});
