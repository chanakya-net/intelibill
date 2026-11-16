import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { origin as admin } from './p01-12-fixture';
import { account, commerce, evidence, merchant, merchantSession, prepareProvider, storedMerchantToken } from './entitlement-browser-fixture';

// Local fixtures only: seeded paid terms, simulated delivery and the local provider console. No live provider is used.
const providerCredentials = join(process.env['P0214_SESSION_DIR']!, 'provider-fixture.json');
const sessionState = (role: 'owner' | 'staff') => join(process.env['P0214_SESSION_DIR']!, role + '.json');
const fixture = JSON.parse(readFileSync(join(evidence, 'fixture.json'), 'utf8')) as {
  term: string; end: string; graceEnds: string; otherTerm: string;
  cancellation: { changeId: string; version: number; effectiveAt: string };
  otherCancellation: { changeId: string; version: number; effectiveAt: string };
};
const merchantPage = merchant + '/billing/accounts/' + account + '/status';
const termPath = (term: string) => '/api/billing/accounts/' + account + '/subscriptions/' + term + '/cancellation';
const withdrawalPath = termPath(fixture.term) + '/withdrawal';
const draftPrefix = 'intelibill.billing.cancellation-withdrawal.v1:';

function observations(phase: string, data: object) {
  writeFileSync(join(evidence, 'observations-' + phase + '.json'), JSON.stringify(data, null, 2));
}

function panel(page: Page, term = fixture.term) {
  return page.getByTestId('account-term').filter({ has: page.locator(`[data-term-id="${term}"]`) });
}

test('@prepare real MFA and merchant sign-in', async ({ page, browser }) => {
  const credentials = await prepareProvider(page);
  writeFileSync(providerCredentials, JSON.stringify(credentials), { mode: 0o600 });
  for (const role of ['owner', 'staff'] as const) {
    const signed = await browser.newPage();
    try {
      await merchantSession(signed, role);
      await signed.context().storageState({ path: sessionState(role) });
    } finally {
      await signed.close();
    }
  }
  observations('prepare', { providerMfa: true, merchantRoles: ['owner', 'staff'] });
});

async function confirmWithdrawal(page: Page) {
  const term = panel(page);
  await term.getByTestId('withdrawal-review').click();
  await expect(term.getByTestId('withdrawal-detail')).toContainText('usual grace period applies again');
  await expect(term.getByTestId('withdrawal-confirm')).toBeDisabled();
  await term.getByTestId('withdrawal-accept').check();
  await term.getByTestId('withdrawal-confirm').click();
}

test('@withdraw explicit confirmation, storage failure and same-key recovery across reload and tabs', async ({ browser }) => {
  const context = await browser.newContext({ storageState: sessionState('owner'), viewport: { width: 1100, height: 900 } });
  const staff = await browser.newPage({ storageState: sessionState('staff') });
  try {
    const page = await context.newPage();
    await page.goto(merchantPage);
    await expect(panel(page).getByTestId('cancellation-saved')).toContainText('scheduled for');
    await expect(panel(page).getByTestId('grace-ends')).toHaveText('No grace period');
    await page.screenshot({ path: join(evidence, 'merchant-scheduled.png'), fullPage: true });
    const posts: unknown[] = [];
    let committed = 0;
    await page.route('**' + withdrawalPath, async route => {
      if (route.request().method() !== 'POST') return route.continue();
      posts.push(route.request().postDataJSON());
      if (posts.length === 1) return route.abort('connectionreset');
      const actual = await fetch(route.request().url(), {
        method: 'POST',
        headers: { Authorization: (await route.request().allHeaders())['authorization'], 'Content-Type': 'application/json' },
        body: route.request().postData(),
      });
      committed = actual.status;
      await route.abort('connectionreset');
    });
    await page.evaluate(prefix => {
      const write = Storage.prototype.setItem;
      Storage.prototype.setItem = function (key, value) {
        if (key.startsWith(prefix)) throw new DOMException('Fixture storage failure', 'QuotaExceededError');
        return write.call(this, key, value);
      };
    }, draftPrefix);
    await confirmWithdrawal(page);
    await expect(panel(page).getByTestId('withdrawal-error')).toContainText('could not save');
    expect(posts).toHaveLength(0);

    await page.reload();
    await confirmWithdrawal(page);
    await expect(panel(page).getByTestId('withdrawal-error')).toContainText('outcome is unknown');
    await page.reload();
    const missing = page.waitForResponse(r => new URL(r.url()).pathname.startsWith(withdrawalPath + '/operations/'));
    await panel(page).getByTestId('withdrawal-check').click();
    expect((await missing).status()).toBe(404);
    await expect(panel(page).getByTestId('withdrawal-pending')).toBeVisible();
    const otherTab = await context.newPage();
    await otherTab.goto(merchantPage);
    await expect(panel(otherTab).getByTestId('withdrawal-check')).toBeVisible();
    await expect(panel(otherTab).getByTestId('withdrawal-review')).toHaveCount(0);

    const operation = await page.evaluate(prefix => {
      const key = Object.keys(localStorage).find(item => item.startsWith(prefix))!;
      return JSON.parse(localStorage.getItem(key)!).command.operationKey as string;
    }, draftPrefix);
    await staff.goto(merchant + '/billing/status');
    const staffToken = await storedMerchantToken(staff);
    const staffLookup = await staff.request.get(merchant + withdrawalPath + '/operations/' + operation,
      { headers: { Authorization: `Bearer ${staffToken}` } });
    expect(staffLookup.status()).toBe(403);

    await panel(page).getByTestId('withdrawal-retry').click();
    await expect.poll(() => committed).toBe(200);
    await expect(panel(page).getByTestId('withdrawal-error')).toContainText('outcome is unknown');
    expect(posts).toHaveLength(2);
    expect(posts[0]).toEqual(posts[1]);
    await page.reload();
    const recovered = page.waitForResponse(r => new URL(r.url()).pathname === withdrawalPath + '/operations/' + operation);
    await panel(page).getByTestId('withdrawal-check').click();
    const receipt = await (await recovered).json();
    await expect(panel(page).getByTestId('withdrawal-done')).toContainText('cancellation was withdrawn');
    await expect(panel(page).getByTestId('withdrawal-propagation')).toContainText('synchronization status');
    await expect(panel(page).getByTestId('cancellation-saved')).toHaveCount(0);
    await expect(panel(page).getByTestId('grace-ends')).not.toHaveText('No grace period');
    await expect(panel(page).getByTestId('sync-state').first()).toHaveText(/waiting/i);
    await page.screenshot({ path: join(evidence, 'merchant-withdrawn.png'), fullPage: true });
    await otherTab.reload();
    await expect(panel(otherTab).getByTestId('withdrawal-check')).toHaveCount(0);
    observations('withdraw', { command: posts[0], receipt, storagePreventedHttp: true, initialLookup: 404,
      retryCommitted: committed, identicalCommand: true, sameOriginSecondTab: true, staffLookup: staffLookup.status() });
  } finally {
    await Promise.all([context.close(), staff.close()]);
  }
});

async function readProvider(page: Page, phase: string) {
  await page.goto(admin + '/accounts');
  await page.getByLabel('Billing account ID').fill(account);
  await page.getByRole('button', { name: 'Read access status' }).click();
  const withdrawn = page.getByRole('region', { name: 'Term ' + fixture.term, exact: true });
  const cancelled = page.getByRole('region', { name: 'Term ' + fixture.otherTerm, exact: true });
  await expect(withdrawn.getByRole('heading')).toHaveText(phase === 'exact-end' ? 'In grace period' : 'Active');
  await expect(cancelled.getByRole('heading')).toHaveText(phase === 'exact-end' ? 'Expired' : 'Active');
  await expect(withdrawn.getByTestId('provider-cancellation-end')).toHaveCount(0);
  await expect(cancelled.getByTestId('provider-cancellation-end')).toBeVisible();
  const sync = phase === 'pending' ? 'Saved, waiting for confirmation' : 'Applied';
  await expect(withdrawn.getByTestId('provider-sync-state')).toHaveText([sync]);
  await page.screenshot({ path: join(evidence, `provider-${phase}.png`), fullPage: true });
  return { withdrawnHeading: await withdrawn.getByRole('heading').textContent(),
    cancelledHeading: await cancelled.getByRole('heading').textContent(), sync };
}

for (const phase of ['pending', 'applied', 'exact-end']) {
  test(`@${phase} both builds show the original grace after withdrawal`, async ({ browser }) => {
    const merchantView = await browser.newPage({ storageState: sessionState('owner'), viewport: { width: 390, height: 844 } });
    const provider = await browser.newPage({ storageState: process.env['P0213_PROVIDER_STATE']!, viewport: { width: 1280, height: 900 } });
    const staff = await browser.newPage({ storageState: sessionState('staff') });
    try {
      const status = merchantView.waitForResponse(r => new URL(r.url()).pathname.endsWith('/billing/accounts/' + account + '/subscriptions'));
      await merchantView.goto(merchantPage);
      const terms = (await (await status).json()).items as { status: { termId: string; graceEndsAt: string | null; cancellation?: unknown } }[];
      const withdrawn = terms.find(item => item.status.termId === fixture.term)!.status;
      const cancelled = terms.find(item => item.status.termId === fixture.otherTerm)!.status;
      expect(withdrawn.cancellation).toBeUndefined();
      expect(Date.parse(withdrawn.graceEndsAt!)).toBe(Date.parse(fixture.graceEnds));
      expect(cancelled.graceEndsAt).toBeNull();
      await expect(panel(merchantView).locator('.state')).toHaveText(phase === 'exact-end' ? 'In grace period' : 'Active');
      await expect(panel(merchantView, fixture.otherTerm).locator('.state')).toHaveText(phase === 'exact-end' ? 'Expired' : 'Active');
      await expect(panel(merchantView, fixture.otherTerm).getByTestId('cancellation-saved')).toBeVisible();
      await expect(panel(merchantView).getByTestId('withdrawal-review')).toHaveCount(0);
      await expect(panel(merchantView, fixture.otherTerm).getByTestId('withdrawal-review')).toHaveCount(phase === 'exact-end' ? 0 : 1);
      const sync = phase === 'pending' ? /waiting/i : /applied/i;
      await expect(panel(merchantView).getByTestId('sync-state')).toHaveText([sync]);
      await merchantView.screenshot({ path: join(evidence, `merchant-${phase}.png`), fullPage: true });
      const providerView = await readProvider(provider, phase);
      const ownerToken = await storedMerchantToken(merchantView);
      const late = phase === 'exact-end' ? await merchantView.request.post(merchant + termPath(fixture.otherTerm) + '/withdrawal', {
        headers: { Authorization: `Bearer ${ownerToken}` },
        data: { operationKey: crypto.randomUUID(), currentVersion: fixture.otherCancellation.version,
          changeId: fixture.otherCancellation.changeId, acceptedEffectiveAt: fixture.otherCancellation.effectiveAt, confirmed: true },
      }) : null;
      if (late) {
        expect(late.status()).toBe(409);
        expect((await late.json()).code).toBe('Billing.CancellationEffective');
      }
      await staff.goto(merchant + '/billing/status');
      const staffToken = await storedMerchantToken(staff);
      const denied = await staff.request.post(merchant + withdrawalPath, { headers: { Authorization: `Bearer ${staffToken}` },
        data: { operationKey: crypto.randomUUID(), currentVersion: 1, changeId: fixture.cancellation.changeId,
          acceptedEffectiveAt: fixture.cancellation.effectiveAt, confirmed: true } });
      expect(denied.status()).toBe(403);
      const stock = await commerce(browser, staff, staffToken, phase, sessionState('owner'));
      observations(phase, { merchant: { withdrawn, cancelled }, provider: providerView, stock, staffWithdrawalDenied: denied.status(),
        lateWithdrawal: late ? { status: late.status(), code: 'Billing.CancellationEffective' } : null });
    } finally {
      await Promise.all([merchantView.close(), provider.close(), staff.close()]);
    }
  });
}
