import { createHash, createHmac, randomBytes, randomInt } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { expect, test, type Browser, type Page } from '@playwright/test';

const origin = process.env['P0108_ADMIN_WEB']!;
const mailpit = process.env['P0108_MAILPIT']!;
const proofFile = process.env['P0108_PROOF_FILE']!;
const sessionFile = process.env['P0108_SESSION_FILE']!;
const restartFile = process.env['P0108_OWNER_ENROLLED']!;
const readyFile = process.env['P0108_INVITER_READY']!;
const mfa = '/api/admin/auth/bootstrap/enrollment/mfa';
const business = '/api/admin/access';
const apiLog = process.env['P0108_API_LOG']!;
const secrets: string[] = [];
const enrollment = '/auth/mfa-enrollment';
const inviteeEmail = 'p0108-invitee@example.test';

test.skip(!origin || !mailpit || !proofFile || !sessionFile, 'Run with the disposable P01-08 fixture');

test('Bootstrap and invitation converge on one-time authenticator enrollment', async ({ browser }) => {
  const owner = await browser.newContext();
  let stage = 'owner bootstrap';
  try {
    const page = await owner.newPage();
    await page.goto(origin + '/auth/bootstrap');
    await enterOwnerPassword(page);
    stage = 'owner MFA';
    await proveEnrollment(page);
    writeFileSync(restartFile, 'ready');
    stage = 'invitation issuance';
    await expect.poll(() => existsSync(readyFile), { timeout: 20_000 }).toBe(true);
    const link = await invite(browser);
    stage = 'invitation acceptance and MFA';
    await acceptAndEnroll(browser, link);
    stage = 'sanitized diagnostic check';
    const diagnostic = readFileSync(apiLog, 'utf8');
    const leaked = secrets.some(value => value.length === 6
      ? diagnostic.includes('"' + value + '"')
      : diagnostic.includes(value));
    if (leaked) throw new Error('Credential-bearing diagnostic');
    console.log('Bootstrap and invitation completed MFA. Zero fixture credential matches in API logs.');
  } catch {
    // Playwright matcher and request errors can include browser state or argument values.
    throw new Error(`P01-08 fixture failed at ${stage}.`);
  } finally {
    await owner.close();
  }
});

async function enterOwnerPassword(page: Page) {
  await page.getByLabel('Setup proof').fill(remember(readFileSync(proofFile, 'utf8').trim()));
  await page.getByLabel('Password', { exact: true }).fill(remember(password()));
  await page.getByRole('button', { name: 'Create owner account' }).click();
  await expect(page).toHaveURL(origin + '/auth/bootstrap/enrollment');
  await expect(page.getByRole('heading', { name: 'MFA setup required' })).toBeVisible();
}

async function invite(browser: Browser) {
  const owner = await browser.newContext();
  try {
    await owner.addCookies([{
      name: 'Intelibill.Admin.FixtureSession', value: readFileSync(sessionFile, 'utf8').trim(), url: origin,
      httpOnly: true, sameSite: 'Strict',
    }]);
    const page = await owner.newPage();
    await page.goto(origin + '/admin-users/invite');
    await page.getByLabel('Administrator email').fill(inviteeEmail);
    await page.getByRole('button', { name: 'Send invitation' }).click();
    await expect(page.getByRole('heading', { name: 'Invitation sent' })).toBeVisible();
    const result = await (await fetch(mailpit + '/api/v1/messages')).json() as { messages: { ID: string }[] };
    expect(result.messages.length).toBe(1);
    const message = await (await fetch(mailpit + '/api/v1/message/' + result.messages[0].ID)).json() as { Text: string };
    const token = /#token=(i1_[A-Za-z0-9_-]{86})/.exec(message.Text)?.[1];
    expect(!!token, 'Mailpit received an invitation').toBe(true);
    return remember(token!);
  } finally {
    await owner.close();
  }
}

async function acceptAndEnroll(browser: Browser, token: string) {
  const invitee = await browser.newContext();
  try {
    const page = await invitee.newPage();
    // Set the fragment before the router starts, without passing a credential as a navigation URL to Playwright.
    await page.addInitScript(({ fragment }) => {
      if (location.pathname === '/auth/invitation' && !location.hash)
        history.replaceState(null, '', '/auth/invitation#token=' + fragment);
    }, { fragment: token });
    await page.goto(origin + '/auth/invitation');
    await page.getByLabel('Invited email').fill(inviteeEmail);
    await page.getByLabel('Password', { exact: true }).fill(remember(password()));
    await page.getByRole('button', { name: 'Accept invitation' }).click();
    await expect(page.getByRole('heading', { name: 'MFA setup required' })).toBeVisible();
    expect(page.url() === origin + '/auth/invitation', 'Invitation fragment cleared').toBe(true);
    await proveEnrollment(page);
  } finally {
    await invitee.close();
  }
}

async function proveEnrollment(page: Page) {
  await page.getByRole('link', { name: 'Set up authenticator' }).click();
  await expect(page).toHaveURL(origin + enrollment);
  const key = page.getByTestId('setup-key');
  await expect(key).toBeVisible();
  const seed = remember((await key.textContent())!);
  expect(/^[A-Z2-7]{32}$/.test(seed), 'Manual setup key displayed').toBe(true);
  await page.reload();
  await expect(key).toBeVisible();
  expect(fingerprint((await key.textContent())!) === fingerprint(seed), 'Pending reload shows the same setup key').toBe(true);

  const attempt = await page.evaluate(async (routes) => {
    const context = await fetch(routes.mfa);
    const { requestToken } = await context.json();
    const access = await fetch(routes.business);
    return { requestToken, status: access.status, noStore: context.headers.get('cache-control') };
  }, { mfa, business });
  expect(attempt.status).toBe(403);
  expect(attempt.noStore).toBe('no-store');
  const credential = (await page.context().cookies()).find(cookie => cookie.name === 'Intelibill.Admin.BootstrapEnrollment');
  expect(!!credential?.httpOnly).toBe(true);
  remember(credential!.value);
  await assertNoStorage(page);

  await enterOtp(page, remember(invalidOtp(seed)));
  await expect(page.getByRole('alert')).toHaveText('Enter a current code from your authenticator app.');
  const proof = remember(otp(seed, Date.now()));
  await enterOtp(page, proof);
  await expect(page.getByRole('heading', { name: 'Authenticator enrolled' })).toBeVisible();
  const codes = page.getByRole('list', { name: 'Recovery codes' }).getByRole('listitem');
  await expect(codes).toHaveCount(10);
  for (const value of await codes.allTextContents()) remember(value);
  await assertNoStorage(page);
  await expect(key).toHaveCount(0);
  await assertReplayDenied(page, attempt.requestToken, proof);
  await assertCleared(page);
}

async function assertReplayDenied(page: Page, requestToken: string, otp: string) {
  const result = await page.evaluate(async (data) => {
    const replay = await fetch(data.mfa + '/verify', {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Bootstrap-CSRF': data.requestToken },
      body: JSON.stringify({ otp: data.otp }),
    });
    return {
      replay: replay.status, business: (await fetch(data.business)).status,
      bodyIsSafe: !(await replay.text()).includes('recoveryCodes'),
      pending: (await fetch(data.mfa)).status,
    };
  }, { mfa, business, requestToken, otp });
  expect(result).toEqual({ replay: 401, business: 401, bodyIsSafe: true, pending: 401 });
}

async function assertNoStorage(page: Page) {
  const storage = await page.evaluate(async () => ({
    local: localStorage.length, session: sessionStorage.length, databases: (await indexedDB.databases()).length,
    caches: (await caches.keys()).length, readableCookies: document.cookie.length,
  }));
  expect(storage).toEqual({ local: 0, session: 0, databases: 0, caches: 0, readableCookies: 0 });
}

async function assertCleared(page: Page) {
  const cookies = await page.context().cookies();
  expect(cookies.some(cookie => cookie.name === 'Intelibill.Admin.BootstrapEnrollment')).toBe(false);
  await page.reload();
  await expect(page.getByText('There is no active enrollment.', { exact: false })).toBeVisible();
  await page.goto(origin + '/');
  await page.goto(origin + enrollment);
  await expect(page.getByRole('list', { name: 'Recovery codes' })).toHaveCount(0);
  await expect(page.getByTestId('setup-key')).toHaveCount(0);
}

async function enterOtp(page: Page, code: string) {
  // Pass the value inside the browser context; assertion diagnostics never format it.
  await page.getByLabel('Authenticator code').evaluate((input: HTMLInputElement, value) => { input.value = value; }, code);
  await page.getByRole('button', { name: 'Verify authenticator' }).click();
}

function remember(value: string) {
  secrets.push(value);
  return value;
}

function password() { return 'Fixture-' + randomBytes(20).toString('hex'); }
function fingerprint(value: string) { return createHash('sha256').update(value).digest('hex'); }

function invalidOtp(seed: string) {
  const now = Date.now();
  const accepted = [-60_000, -30_000, 0, 30_000, 60_000].map(offset => otp(seed, now + offset));
  let value: string;
  do { value = String(randomInt(1_000_000)).padStart(6, '0'); } while (accepted.includes(value));
  return value;
}

function otp(seed: string, timestamp: number) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const bits = [...seed].map(character => alphabet.indexOf(character).toString(2).padStart(5, '0')).join('');
  const bytes = Buffer.from(bits.match(/.{8}/g)!.map(byte => parseInt(byte, 2)));
  const step = Buffer.alloc(8);
  step.writeBigUInt64BE(BigInt(Math.floor(timestamp / 30_000)));
  const mac = createHmac('sha1', bytes).update(step).digest();
  return String((mac.readUInt32BE(mac.at(-1)! & 15) & 0x7fffffff) % 1_000_000).padStart(6, '0');
}
