import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { fillSecret } from './p01-12-fixture';
import { enroll, loopback, origin, password, required, scratch, signIn } from './p01-15-provider';

const account = required('P0702_ACCOUNT');
const mailpit = loopback('P0115_MAILPIT');
const apiPath = '/api/admin/accounts/' + account + '/notifications';

async function captures(query: string) {
  const url = new URL('/api/v1/search', mailpit);
  url.searchParams.set('query', query);
  const response = await fetch(url);
  expect(response.ok).toBe(true);
  return (await response.json()).messages as { ID: string }[];
}

async function capturedText(address: string) {
  const rows = await captures('to:' + address);
  expect(rows).toHaveLength(1);
  const response = await fetch(mailpit + '/api/v1/message/' + rows[0].ID);
  return (await response.json()).Text as string;
}

async function bootstrap(page: Page) {
  await page.goto(origin + '/auth/bootstrap');
  const chosen = password();
  await fillSecret(page, 'Setup proof', readFileSync(join(scratch, 'proof'), 'utf8').trim());
  await fillSecret(page, 'Password', chosen);
  await page.getByRole('button', { name: 'Create owner account' }).click();
  await expect(page.getByRole('heading', { name: 'MFA setup required' })).toBeVisible();
  const credentials = await enroll(page, 'operator@p0702.test', chosen);
  await signIn(page, credentials);
}

async function invited(owner: Page, operator: Page, role: 'Billing' | 'Support') {
  const email = role.toLowerCase() + '-operator@p0702.test';
  await owner.goto(origin + '/');
  await owner.getByRole('link', { name: 'Manage administrator invitations' }).click();
  await owner.getByLabel('Administrator email').fill(email);
  await owner.getByRole('button', { name: 'Send invitation' }).click();
  await expect(owner.getByRole('heading', { name: 'Invitation sent' })).toBeVisible();
  const token = /#token=(i1_[A-Za-z0-9_-]{86})/.exec(await capturedText(email))?.[1];
  expect(Boolean(token)).toBe(true);
  await operator.goto(origin + '/auth/invitation#token=' + token);
  await operator.getByLabel('Invited email').fill(email);
  const chosen = password();
  await fillSecret(operator, 'Password', chosen);
  await operator.getByRole('button', { name: 'Accept invitation' }).click();
  await expect(operator.getByRole('heading', { name: 'MFA setup required' })).toBeVisible();
  const credentials = await enroll(operator, email, chosen);
  await signIn(operator, credentials);
  expect((await operator.request.get(origin + apiPath)).status()).toBe(403);

  await owner.goto(origin + '/');
  await owner.getByRole('link', { name: 'Manage administrator access' }).click();
  const directory = await owner.request.get(origin + '/api/admin/users');
  expect(directory.status()).toBe(200);
  const users = await directory.json() as { items: { id: string; email: string }[] };
  const user = users.items.find(item => item.email.toLowerCase() === email);
  if (!user) throw new Error('Enrolled fixture operator absent from administrator directory');
  await owner.getByLabel('Administrator', { exact: true }).selectOption(user.id);
  await owner.getByRole('checkbox', { name: role, exact: true }).check();
  await owner.getByLabel('Reason', { exact: true }).selectOption('responsibilities-changed');
  await owner.getByLabel('I confirm these access changes.').check();
  await owner.getByRole('button', { name: 'Save administrator access' }).click();
  await expect(owner.getByRole('status')).toHaveText('Administrator access saved.');
  await signIn(operator, credentials);
}

async function loadPreview(page: Page) {
  await page.goto(origin + '/accounts');
  const response = page.waitForResponse(reply => new URL(reply.url()).pathname === apiPath);
  await page.getByLabel('Billing account ID').fill(account);
  await page.getByRole('button', { name: 'Read access status' }).click();
  expect((await response).status()).toBe(200);
  await expect(page.getByTestId('provider-notification-row').first()).toBeVisible();
}

async function inspectCapture(page: Page) {
  const row = page.getByTestId('provider-notification-row')
    .filter({ has: page.getByTestId('email-state').filter({ hasText: 'Accepted by SMTP' }) })
    .filter({ hasText: 'Commercial (Billing' }).first();
  const capture = row.getByTestId('mailpit-capture');
  await capture.focus();
  await expect(capture).toBeFocused();
  const open = page.context().waitForEvent('page');
  await page.keyboard.press('Enter');
  const tab = await open;
  await tab.waitForLoadState();
  const url = new URL(tab.url());
  expect(url.origin).toBe(mailpit);
  const query = url.searchParams.get('q')!;
  expect(query).toMatch(/^message-id:[0-9a-f]{32}@notices\.intelibill\.invalid$/);
  const messages = await captures(query);
  expect(messages).toHaveLength(1);
  const detail = await (await fetch(mailpit + '/api/v1/message/' + messages[0].ID)).json();
  expect(detail.Subject).toBe('सदस्यता नवीनीकरण अनुस्मारक');
  expect(detail.To.map((to: {Address: string}) => to.Address)).toEqual(['owner@p0702.test']);
  expect(detail.From.Address).toBe('billing@intelibill.test');
  expect(detail.ReturnPath).toBe('billing@intelibill.test');
  expect(detail.Cc ?? []).toHaveLength(0);
  expect(detail.Bcc ?? []).toHaveLength(0);
  expect(detail.ReplyTo[0].Address).toBe('support@intelibill.test');
  expect(detail.Text).toContain('/billing/accounts/' + account + '/status');
  const preview = await (await page.request.get(origin + apiPath)).json();
  const notice = preview.items.find((item: { emailDelivery?: { captureUrl?: string } }) =>
    item.emailDelivery?.captureUrl === url.href);
  expect(Boolean(notice)).toBe(true);
  const utc = notice.effectiveDeadlineAt.replace(/(?:\.0+)?(?:Z|\+00:00)$/, '');
  expect(detail.Text).toContain(utc + '.0000000+00:00 UTC');
  expect(detail.Text).toContain('IST');
  expect(detail.Text).toContain('INR 1,499.00');
  await expect(tab.getByText(detail.Subject, { exact: true }).first()).toBeVisible();
  await tab.close();
}

test('Operator follows the localized notice to its local capture', async ({ browser, page }) => {
  await bootstrap(page);
  const billing = await browser.newPage();
  const support = await browser.newPage({ viewport: { width: 390, height: 844 } });
  try {
    await invited(page, billing, 'Billing');
    await loadPreview(billing);
    await billing.reload();
    await loadPreview(billing);
    await expect(billing.getByTestId('email-state').filter({ hasText: 'SMTP outcome unknown' })).toHaveCount(1);
    await inspectCapture(billing);
    await billing.setViewportSize({ width: 390, height: 844 });
    await billing.getByTestId('provider-notifications-locale').selectOption('hi-IN');
    await expect(billing.getByTestId('email-state').filter({ hasText: 'SMTP परिणाम अज्ञात' })).toBeVisible();
    expect(await billing.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
    await invited(page, support, 'Support');
    await loadPreview(support);
    await expect(support.getByTestId('email-detail')).toHaveCount(0);
    expect(await captures('to:staff@p0702.test')).toHaveLength(0);
    expect(await captures('to:other@p0702.test')).toHaveLength(0);
    expect(await captures('to:manager@p0702.test')).toHaveLength(1);
    const shop = await capturedText('shop@p0702.test');
    expect(shop).not.toContain(account);
    expect(shop).not.toContain('/billing');
    expect(shop).toMatch(/[\u0B80-\u0BFF]/);
    const contacts = await captures('to:contact@p0702.test');
    expect(contacts).toHaveLength(2);
    const contactMessages = await Promise.all(contacts.map(async item =>
      (await fetch(mailpit + '/api/v1/message/' + item.ID)).json()));
    const reminder = contactMessages.find(item => item.Subject === 'সাবস্ক্রিপশন নবায়ন স্মারক');
    expect(Boolean(reminder)).toBe(true);
    expect(reminder.Text).not.toContain('/billing/accounts');
    expect(reminder.Text).not.toContain(account);
    writeFileSync(join(required('P0702_EVIDENCE_DIR'), 'browser.txt'), [
      'Real Chromium and source-built services. Fixture capabilities granted after independent MFA enrollment.',
      'Billing preview: accepted and uncertain. Support: redacted. No grant: HTTP 403.',
      'Keyboard-opened Mailpit search returned one matching Hindi owner capture; From, To, Reply-To and account link matched.',
      'Tamil shop owner capture; Bengali opt-in contact proof and reminder; zero staff and foreign-account captures.',
      'Reload, 390px layout and localized uncertainty verified. Process loss did not trigger a second manager submission.',
    ].join('\n'));
  } finally {
    await billing.close();
    await support.close();
  }
});
