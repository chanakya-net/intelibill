import { expect, test, type Page } from '@playwright/test';
import {
  accessPath, adminPost, createOperator, createOwner, enrollInBrowser, fillSecret, mail, origin, proofFile,
  refreshPath, sessionPath, signIn, type Enrolled,
} from './p01-12-fixture';

const evidenceReference = 'SEC-2026-0412';

test.skip(!origin || !mail || !proofFile, 'Run with the disposable P01-12 fixture');

test('recovery code and reviewed reset end sessions and require enrollment on source API and PostgreSQL',
  async ({ browser }) => {
    const pages: Page[] = [];
    const page = async () => {
      const opened = await (await browser.newContext({ viewport: { width: 1280, height: 800 } })).newPage();
      pages.push(opened);
      return opened;
    };
    try {
      const owner = await page();
      let ownerAccount = await signIn(owner, await createOwner(owner));
      const operator = await page();
      let operatorAccount = await signIn(operator, await createOperator(owner, operator));

      const resetCode = await reviewReset(owner, operatorAccount.email);
      await assertEnded(operator, 'operator');
      const recovering = await page();
      operatorAccount = await recover(recovering, operatorAccount, 'reset', resetCode);
      operatorAccount = await signIn(recovering, operatorAccount);
      expect((await adminPost(await page(), '/api/admin/auth/recovery/reset',
        { email: operatorAccount.email, password: operatorAccount.password, resetToken: resetCode })).status()).toBe(401);
      console.log('Reviewed reset: stale access/refresh 401, enrollment-only 403, re-enrollment signed in, replay 401.');

      const ownerRecovering = await page();
      const [usedCode] = ownerAccount.codes;
      ownerAccount = await recover(ownerRecovering, ownerAccount, 'code', usedCode);
      await assertEnded(owner, 'owner');
      ownerAccount = await signIn(ownerRecovering, ownerAccount);
      expect((await adminPost(await page(), '/api/admin/auth/recovery/code',
        { email: ownerAccount.email, password: ownerAccount.password, recoveryCode: usedCode })).status()).toBe(401);
      console.log('Recovery code: stale access/refresh 401, enrollment-only 403, re-enrollment signed in, replay 401.');

      await assertSafeAudit(ownerRecovering, [resetCode, usedCode, ownerAccount.password, operatorAccount.password,
        evidenceReference, ownerAccount.email, operatorAccount.email]);
    } finally {
      await Promise.all(pages.map((opened) => opened.context().close()));
    }
  });

async function reviewReset(owner: Page, email: string) {
  await owner.getByRole('link', { name: 'Review administrator recovery' }).click();
  await owner.getByLabel('Administrator email').fill(email);
  await owner.getByLabel('Reason').selectOption('authenticator-lost');
  await owner.getByLabel('How you verified the person').selectOption('video-call');
  await owner.getByLabel('Evidence reference').fill(evidenceReference);
  await owner.getByLabel('I verified this person out of band and reviewed this reset.').check();
  await owner.getByRole('button', { name: 'Reset authenticator' }).click();
  const code = (await owner.getByTestId('reset-code').textContent())!.trim();
  expect(code).toMatch(/^k1_[A-Za-z0-9_-]{86}$/);
  await owner.getByRole('button', { name: 'I handed over the code' }).click();
  await expect(owner.getByTestId('reset-code')).toHaveCount(0);
  return code;
}

async function recover(page: Page, account: Enrolled, method: 'code' | 'reset', proof: string) {
  await page.goto(origin + '/auth/sign-in');
  await page.getByRole('link', { name: 'Lost your authenticator? Recover access' }).click();
  await expect(page).toHaveURL(origin + '/auth/recovery');
  if (method === 'reset') await page.getByLabel('A reset issued by a platform owner').check();
  await page.getByLabel('Email', { exact: true }).fill(account.email);
  await fillSecret(page, 'Password', account.password);
  await page.locator('#recovery-proof').fill(proof);
  await page.getByRole('button', { name: 'Recover access' }).click();
  await expect(page).toHaveURL(origin + '/auth/mfa-enrollment');
  await expect(page.getByTestId('setup-key')).toBeVisible();
  // The enrollment boundary: an authenticated enrollment credential, never business access.
  expect((await page.request.get(origin + accessPath)).status()).toBe(403);
  expect((await page.request.get(origin + sessionPath)).status()).toBe(403);
  return enrollInBrowser(page, account);
}

async function assertEnded(page: Page, label: string) {
  expect((await page.request.get(origin + sessionPath)).status(), label + ' stale access').toBe(401);
  expect((await adminPost(page, refreshPath)).status(), label + ' stale refresh').toBe(401);
  await page.goto(origin + '/');
  await expect(page).toHaveURL(origin + '/auth/sign-in');
}

async function assertSafeAudit(page: Page, secrets: string[]) {
  const response = await page.request.get(origin + '/api/admin/audit?pageSize=100');
  expect(response.status()).toBe(200);
  const body = await response.text();
  for (const action of ['recovery-reset-issued', 'recovery-reset-redeemed', 'recovery-code-redeemed', 'device-session-revoked']) {
    expect(body).toContain(action);
  }
  for (const secret of secrets) expect(body.toLowerCase()).not.toContain(secret.toLowerCase());
  await page.getByRole('link', { name: 'Audit history' }).click();
  await expect(page.locator('[aria-label="Audit events"]').getByText('Recovery reset issued').first()).toBeVisible();
}
