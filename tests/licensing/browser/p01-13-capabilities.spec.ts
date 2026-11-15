import { execFileSync } from 'node:child_process';
import { expect, test, type Page } from '@playwright/test';
import {
  adminPost, createOperator, createOwner, mail, origin, proofFile, refreshPath, sessionPath, signIn,
} from './p01-12-fixture';

const usersPath = '/api/admin/users';
const dbContainer = process.env['P0113_DB_CONTAINER'];
const editLink = 'Manage administrator access';
const confirmation = 'I confirm these access changes.';
const allGrants = ['Support', 'Billing', 'Licensing Manager', 'Security Administrator', 'Platform Owner'];

test.skip(!origin || !mail || !proofFile || !dbContainer, 'Run with the disposable P01-13 fixture');

test('owner delegates capabilities through the admin console', async ({ browser }) => {
  const ownerContext = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const operatorContext = await browser.newContext({ viewport: { width: 390, height: 844 } });
  const foreignContext = await browser.newContext();
  try {
    const owner = await ownerContext.newPage();
    const operator = await operatorContext.newPage();
    const foreign = await foreignContext.newPage();
    let ownerAccount = await signIn(owner, await createOwner(owner));
    let operatorAccount = await signIn(operator, await createOperator(owner, operator));
    await assertRefused(operator, foreign);
    await owner.getByRole('link', { name: editLink }).click();
    await choose(owner, ownerAccount.email);
    await grant(owner, 'Platform Owner').uncheck();
    await confirmAndSave(owner);
    await expect(owner.getByRole('alert')).toContainText('no recoverable platform owner');

    const target = await choose(owner, operatorAccount.email);
    await expectGrants(owner, []);

    await grant(owner, 'Support').check();
    await owner.getByLabel('Reason', { exact: true }).selectOption('responsibilities-changed');
    await save(owner).click();
    await expect(owner.getByLabel(confirmation)).not.toBeChecked();
    expect((await administrator(owner, operatorAccount.email)).version).toBe(target.version);

    const { sessionId } = await (await owner.request.get(origin + sessionPath)).json();
    ageMfa(sessionId);
    await confirmAndSave(owner);
    await expect(owner.getByRole('alert')).toContainText('submit the change within five minutes');
    await expect(owner.getByRole('link', { name: 'Sign in again' })).toBeVisible();
    expect((await administrator(owner, operatorAccount.email)).version).toBe(target.version);
    ownerAccount = await signIn(owner, ownerAccount);
    await owner.getByRole('link', { name: editLink }).click();
    await choose(owner, operatorAccount.email);
    await expectGrants(owner, []);
    await grant(owner, 'Support').check();
    await grant(owner, 'Platform Owner').check();
    await confirmAndSave(owner);
    await expect(owner.getByRole('status')).toHaveText('Administrator access saved.');
    await owner.reload();
    await choose(owner, operatorAccount.email);
    await expectGrants(owner, ['Support', 'Platform Owner']);
    await assertEnded(operator);
    operatorAccount = await signIn(operator, operatorAccount);
    await expect(operator.getByRole('link', { name: editLink })).toBeVisible();
    await expect(operator.getByRole('list', { name: 'Granted capabilities' }).getByText('Support')).toBeVisible();
    console.log('Fresh password/TOTP was required after stale MFA; explicit resubmit saved Support and Platform Owner, both persisted after reload. The old access and refresh returned 401.');

    await assertVersionConflict(owner, operatorAccount.email);
    await assertEnded(operator);
    operatorAccount = await signIn(operator, operatorAccount);
    await choose(owner, operatorAccount.email);
    await expectGrants(owner, ['Billing', 'Platform Owner']);
    await grant(owner, 'Platform Owner').uncheck();
    await confirmAndSave(owner);
    await expect(owner.getByRole('status')).toHaveText('Administrator access saved.');
    await assertEnded(operator);
    await signIn(operator, operatorAccount);
    const session = await (await operator.request.get(origin + sessionPath)).json();
    expect(session.capabilities).toEqual(['billing']);
    await expect(operator.getByRole('link', { name: editLink })).toHaveCount(0);
    await assertAudit(owner);
    console.log('Version conflict cleared the editor and reloaded current grants. Demotion ended the delegated session, and re-sign-in returned Billing alone. Three successful changes exposed closed grant evidence in audit.');
  } finally {
    await Promise.all([ownerContext.close(), operatorContext.close(), foreignContext.close()]);
  }
});

async function assertRefused(operator: Page, foreign: Page) {
  await expect(operator.getByRole('link', { name: editLink })).toHaveCount(0);
  await operator.goto(origin + '/admin-users');
  await expect(operator.getByRole('alert')).toHaveText('You cannot manage administrator access.');
  expect((await operator.request.get(origin + usersPath)).status()).toBe(403);

  await foreign.context().addCookies([{
    name: 'Intelibill.Admin.FixtureSession', value: 'p0113-local-scope-fixture', url: origin,
  }]);
  const refused = await foreign.request.get(origin + usersPath);
  expect(refused.status()).toBe(401);
  expect(refused.headers()['cache-control']).toBe('no-store');
  await foreign.goto(origin + '/admin-users');
  await expect(foreign).toHaveURL(origin + '/auth/sign-in');
  console.log('No-grant provider session saw the refused editor and HTTP 403. A local fixture credential asserting manage access got HTTP 401 and the sign-in screen.');
}

async function assertVersionConflict(owner: Page, email: string) {
  const prior = await choose(owner, email);
  await expectGrants(owner, ['Support', 'Platform Owner']);
  expect((await put(owner, prior, ['billing', 'platform-owner'])).status()).toBe(200);
  await grant(owner, 'Platform Owner').uncheck();
  await confirmAndSave(owner);
  await expect(owner.getByRole('alert')).toHaveText('Administrator access changed. Reload before editing.');
  await expect(owner.getByRole('group', { name: 'Capability grants' })).toHaveCount(0);
  await owner.getByRole('button', { name: 'Reload administrators' }).click();
}

async function assertEnded(page: Page) {
  expect((await page.request.get(origin + sessionPath)).status()).toBe(401);
  expect((await adminPost(page, refreshPath)).status()).toBe(401);
}

async function assertAudit(owner: Page) {
  const response = await owner.request.get(origin + '/api/admin/audit?action=admin-capabilities-changed');
  expect(response.status()).toBe(200);
  const { items } = await response.json();
  expect(items).toHaveLength(3);
  expect(items[0].after.capabilities).toEqual(['billing']);
  expect(items[0].before.capabilities).toEqual(['billing', 'platform-owner']);
  expect(items[0].after.version).not.toBe(items[0].before.version);
  expect(items[0].reasonCode).toBe('AdminResponsibilitiesChanged');
  await owner.getByRole('link', { name: 'Provider Console' }).click();
  await owner.getByRole('link', { name: 'Audit history' }).click();
  await expect(owner.getByText('Administrator capabilities changed', { exact: true }).last()).toBeVisible();
}

function ageMfa(sessionId: string) {
  execFileSync('docker', [
    'exec', '-i', dbContainer!, 'psql', '-Xq', '-U', 'postgres', '-d', 'licensing',
    '-v', 'ON_ERROR_STOP=1', '-v', 'session_id=' + sessionId,
  ], { input: "UPDATE admin_sessions SET created_at=now()-interval '6 minutes', "
    + "mfa_verified_at=now()-interval '6 minutes' WHERE id=:'session_id';" });
}

async function administrator(page: Page, email: string): Promise<{ id: string; version: number }> {
  const response = await page.request.get(origin + usersPath);
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toBe('no-store');
  const { items } = await response.json();
  return items.find((row: { email: string }) => row.email.toLowerCase() === email.toLowerCase());
}

async function choose(page: Page, email: string) {
  const row = await administrator(page, email);
  await page.getByLabel('Administrator', { exact: true }).selectOption(row.id);
  await expect(page.getByRole('group', { name: 'Capability grants' })).toBeVisible();
  return row;
}

function grant(page: Page, label: string) {
  return page.getByRole('checkbox', { name: label, exact: true });
}

function save(page: Page) {
  return page.getByRole('button', { name: 'Save administrator access' });
}

async function expectGrants(page: Page, assigned: string[]) {
  for (const label of allGrants) {
    if (assigned.includes(label)) await expect(grant(page, label)).toBeChecked();
    else await expect(grant(page, label)).not.toBeChecked();
  }
}

async function confirmAndSave(page: Page) {
  await page.getByLabel('Reason', { exact: true }).selectOption('responsibilities-changed');
  await page.getByLabel(confirmation).check();
  await save(page).click();
}

async function put(page: Page, row: { id: string; version: number }, capabilities: string[]) {
  const context = await page.request.get(origin + '/api/admin/auth/sign-in/context');
  const { requestToken } = await context.json();
  return page.request.put(origin + usersPath + '/' + row.id + '/capabilities', {
    data: { capabilities, expectedVersion: row.version, reason: 'responsibilities-changed', confirmed: true },
    headers: { Origin: origin, 'X-Admin-CSRF': requestToken },
  });
}
