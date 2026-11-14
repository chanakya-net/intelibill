import { randomBytes } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { expect, test, type Browser, type Locator, type Page } from '@playwright/test';

// LOCAL FIXTURE ONLY: the owner's provider session is the environment-guarded fixture,
// and the recipient is a synthetic address captured by the stack's Mailpit.
const admin = process.env['P0107_ADMIN_WEB'];
const mailpit = process.env['P0107_MAILPIT'];
const sessionFile = process.env['P0107_SESSION_FILE'];

test.skip(!admin || !mailpit || !sessionFile, 'Run in the isolated P01-07 fixture with recording disabled');

test('owner invites an administrator who enrolls only as far as MFA setup', async ({ browser }) => {
  const invitee = `invitee.${randomBytes(6).toString('hex')}@example.test`;
  const owner = await browser.newContext();
  await owner.addCookies([{
    name: 'Intelibill.Admin.FixtureSession',
    value: readFileSync(sessionFile!, 'utf8').trim(),
    url: admin!,
    httpOnly: true,
    sameSite: 'Strict',
  }]);
  const ownerPage = await owner.newPage();
  await ownerPage.goto(admin + '/admin-users/invite');
  await ownerPage.getByLabel('Administrator email').fill(invitee);
  const issued = ownerPage.waitForResponse(response =>
    new URL(response.url()).pathname === '/api/admin/invitations' && response.request().method() === 'POST');
  await ownerPage.getByRole('button', { name: 'Send invitation' }).click();
  expect((await issued).status()).toBe(201);
  await expect(ownerPage.getByRole('heading', { name: 'Invitation sent' })).toBeVisible();
  await owner.close();

  const link = await readInvitationLink(invitee);
  expect(new URL(link).pathname).toBe('/auth/invitation');

  const invited = await browser.newContext();
  const page = await invited.newPage();
  await page.goto(link);
  await expect(page.getByRole('heading', { name: 'Accept your invitation' })).toBeVisible();
  expect(page.url()).toBe(admin + '/auth/invitation');
  await page.getByLabel('Invited email').fill(invitee);
  await enterSecret(page.getByLabel('Password'), 'Fixture-' + randomBytes(24).toString('base64url'));
  await page.getByRole('button', { name: 'Accept invitation' }).click();
  await pending(page);
  await assertRestricted(page);
  await page.reload();
  await pending(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: process.env['P0105_ARTIFACTS'] + '/p0107-enrollment-narrow.png' });
  await invited.close();

  await expect(replay(browser, link, invitee)).resolves.toContain('expired or was already used');
  console.log('P01-07 fixture: owner issued 201, Mailpit link accepted once in a fresh context, MFA pending, business 403, replay refused');
});

async function readInvitationLink(recipient: string): Promise<string> {
  for (let attempt = 0; attempt < 50; attempt++) {
    const search = await fetch(`${mailpit}/api/v1/search?query=${encodeURIComponent(`to:"${recipient}"`)}`);
    const { messages } = await search.json() as { messages: { ID: string }[] };
    if (messages.length === 1) {
      const message = await (await fetch(`${mailpit}/api/v1/message/${messages[0].ID}`)).json() as { Text: string };
      const match = new RegExp(`${admin}/auth/invitation#token=i1_[A-Za-z0-9_-]{86}`).exec(message.Text);
      expect(match).not.toBeNull();
      return match![0];
    }
    await new Promise(resolve => setTimeout(resolve, 200));
  }
  throw new Error('The invitation message did not reach Mailpit');
}

async function replay(browser: Browser, link: string, invitee: string): Promise<string> {
  const context = await browser.newContext();
  try {
    const page = await context.newPage();
    await page.goto(link);
    await page.getByLabel('Invited email').fill(invitee);
    await enterSecret(page.getByLabel('Password'), 'Fixture-' + randomBytes(24).toString('base64url'));
    await page.getByRole('button', { name: 'Accept invitation' }).click();
    return await page.getByRole('alert').innerText();
  } finally {
    await context.close();
  }
}

async function enterSecret(input: Locator, value: string) {
  await input.evaluate((element: HTMLInputElement, secret) => {
    element.value = secret;
  }, value);
}

async function pending(page: Page) {
  await expect(page.getByRole('heading', { name: 'MFA setup required' })).toBeVisible();
  await expect(page.getByText('No provider permissions')).toBeVisible();
  await expect(page.getByLabel('Password')).toHaveCount(0);
}

async function assertRestricted(page: Page) {
  const access = await page.evaluate(async () => {
    const business = await fetch('/api/admin/access');
    const invitations = await fetch('/api/admin/invitations/context');
    return {
      business: business.status,
      invitations: invitations.status,
      noStore: business.headers.get('cache-control'),
      localKeys: Object.keys(localStorage).length,
      sessionKeys: Object.keys(sessionStorage).length,
    };
  });
  expect(access).toEqual({ business: 403, invitations: 401, noStore: 'no-store', localKeys: 0, sessionKeys: 0 });
}
