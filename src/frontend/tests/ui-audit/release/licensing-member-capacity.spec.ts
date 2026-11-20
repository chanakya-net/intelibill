import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test, type Locator, type Page, type Route } from '@playwright/test';

import { accessibilityFixtures } from './accessibility-localization-fixtures';
import { session, SHOP_A, startPurchaseReceivingServer, USER } from './purchase-receiving-server';

/**
 * P04-11 merchant flows against local API fixtures (production-built assets). Real seat counting,
 * locking and races are proven on PostgreSQL by P0411LicensingTests; this spec shows what each
 * person sees when a seat is refused and that the supported action can be retried.
 */
const BUILD = resolve(__dirname, '../../../dist/INVENTORY/browser');
const DESKTOP = { width: 1440, height: 900 };
const MOBILE = { width: 390, height: 844 };
const CAPACITY = 'Licence.MemberCapacityReached';
const SHOP_FULL = 'This shop has no free member seat right now.';
const OWNER_FULL = 'All member seats are in use: 3 of 3 taken.';
const INVITATION = { invitationId: 'invitation-p0411', token: 'P0411-SECRET-TOKEN' };

type Member = { userId: string; firstName: string; lastName: string; role: string; status: string; version: number };
type Event = { method: string; path: string; status: number; code?: string; memberCapacity?: unknown };

test.use({ serviceWorkers: 'block', reducedMotion: 'reduce' });

const json = (route: Route, body: unknown, status = 200) =>
  route.fulfill({ status, json: body, headers: { 'Cache-Control': 'no-store' } });

function initialMembers(): Member[] {
  return [
    { userId: USER, firstName: 'Receiving', lastName: 'Owner', role: 'Owner', status: 'Active', version: 0 },
    { userId: 'member-asha', firstName: 'Asha', lastName: 'Active', role: 'Staff', status: 'Active', version: 0 },
    { userId: 'member-ravi', firstName: 'Ravi', lastName: 'Active', role: 'Staff', status: 'Active', version: 0 },
    { userId: 'member-sunil', firstName: 'Sunil', lastName: 'Suspended', role: 'Staff', status: 'Suspended', version: 1 },
  ];
}

async function installFixture(page: Page) {
  await accessibilityFixtures(page);
  const state = { allowance: 3, inviteeSeatFree: false, members: initialMembers() };
  const requests: Event[] = [];
  const respond = (route: Route, body: unknown, status: number, extra: Partial<Event> = {}) => {
    requests.push({ method: route.request().method(), path: new URL(route.request().url()).pathname, status, ...extra });
    return json(route, body, status);
  };
  const active = () => state.members.filter((member) => member.status === 'Active').length;
  const refusal = (memberCapacity?: object) =>
    ({ status: 409, title: CAPACITY, code: CAPACITY, ...(memberCapacity ? { memberCapacity } : {}) });

  await page.route('**/api/users', (route) => respond(route, state.members.map((member) => ({
    userId: member.userId, firstName: member.firstName, lastName: member.lastName,
    email: `${member.userId}@example.test`, phoneNumber: null, role: member.role, status: member.status,
    membershipVersion: member.version, shopIds: [SHOP_A],
  })), 200));
  await page.route(`**/api/shops/${SHOP_A}/invitations`, (route) => respond(route, [], 200));
  await page.route('**/api/auth/token/refresh', (route) => respond(route, session(), 200));
  await page.route(`**/api/invitations/${INVITATION.invitationId}/accept`, (route) => {
    // The invitee is told the shop is full, never how many seats it has.
    if (!state.inviteeSeatFree) return respond(route, refusal(), 409, { code: CAPACITY });
    return respond(route, {
      userId: 'member-new', shopId: SHOP_A, displayName: 'New Member', role: 'Staff', status: 'Active', version: 0,
    }, 200);
  });
  await page.route(`**/api/shops/${SHOP_A}/members/*`, (route) => {
    const userId = new URL(route.request().url()).pathname.split('/').pop()!;
    const target = state.members.find((member) => member.userId === userId)!;
    const body = route.request().postDataJSON() as { role: string; status: string; expectedVersion: number };
    if (target.status !== 'Active' && body.status === 'Active' && active() >= state.allowance) {
      const memberCapacity = { memberAllowance: state.allowance, activeMembers: active() };
      return respond(route, refusal(memberCapacity), 409, { code: CAPACITY, memberCapacity });
    }
    Object.assign(target, { role: body.role, status: body.status, version: target.version + 1 });
    return respond(route, {
      userId, shopId: SHOP_A, displayName: `${target.firstName} ${target.lastName}`,
      role: target.role, status: target.status, version: target.version,
    }, 200);
  });

  return { state, requests, active };
}

let server: Awaited<ReturnType<typeof startPurchaseReceivingServer>>;
let fixture: Awaited<ReturnType<typeof installFixture>>;

test.beforeEach(async ({ page }) => {
  server = await startPurchaseReceivingServer(BUILD);
  fixture = await installFixture(page);
  await page.addInitScript((value) => {
    const key = 'inventory.auth.session.local';
    if (!localStorage.getItem(key)) localStorage.setItem(key, JSON.stringify(value));
  }, session());
});

test.afterEach(async ({ page, browser }, info) => {
  mkdirSync(info.outputDir, { recursive: true });
  writeFileSync(info.outputPath('fixture-member-capacity-http.json'), JSON.stringify({
    source: 'Local API fixtures with production-built merchant assets (not a live API)',
    viewport: page.viewportSize(), browser: browser.version(), shop: SHOP_A,
    requests: fixture.requests, activeMembers: fixture.active(), members: fixture.state.members,
  }, null, 2) + '\n');
  await server.close();
});

const editButton = (page: Page, name: string) =>
  page.locator('tr, article').filter({ hasText: name })
    .getByRole('button', { name: 'Edit' }).filter({ visible: true }).first();

async function editMember(page: Page, name: string, change: { role?: string; status?: string }): Promise<Locator> {
  await editButton(page, name).click();
  const dialog = page.getByRole('dialog');
  if (change.role) await dialog.locator('select[formControlName="role"]').selectOption(change.role);
  if (change.status) await dialog.locator('select[formControlName="status"]').selectOption(change.status);
  await dialog.getByRole('button', { name: 'Save Changes' }).click();
  return dialog;
}

for (const [layout, viewport] of [['desktop', DESKTOP], ['mobile', MOBILE]] as const) {
  test(`P04-11 invitee at a full shop keeps a redeemable invitation on ${layout}`, async ({ page }, info) => {
    await page.setViewportSize(viewport);
    await page.goto(`${server.origin}/invitations/accept#invitationId=${INVITATION.invitationId}&token=${INVITATION.token}`);
    const accept = page.getByRole('button', { name: 'Accept Invitation' });
    await expect(accept).toBeEnabled();
    expect(page.url()).not.toContain(INVITATION.token);

    await accept.click();
    await expect(page.getByText(SHOP_FULL)).toBeVisible();
    await expect(page.getByText(/of \d+ taken/)).toHaveCount(0);
    await expect(accept).toBeEnabled();
    expect(page.url()).not.toContain(INVITATION.token);
    await page.screenshot({ path: info.outputPath(`invitee-shop-full-${layout}.png`) });

    // The owner frees a seat; the same link is then accepted.
    fixture.state.inviteeSeatFree = true;
    await accept.click();
    await expect(page.getByText('Invitation accepted')).toBeVisible();
    await page.screenshot({ path: info.outputPath(`invitee-accepted-after-seat-freed-${layout}.png`) });
    const accepts = fixture.requests.filter(({ path }) => path.endsWith('/accept'));
    expect(accepts).toMatchObject([{ status: 409, code: CAPACITY }, { status: 200 }]);
    expect(accepts[0].memberCapacity).toBeUndefined();
  });

  test(`P04-11 owner sees seat usage, frees a seat and reinstates on ${layout}`, async ({ page }, info) => {
    await page.setViewportSize(viewport);
    await page.goto(server.origin + '/users');
    await expect(editButton(page, 'Sunil Suspended')).toBeVisible();

    // A role-only change at the allowance takes no new seat.
    await expect(await editMember(page, 'Asha Active', { role: 'Manager' })).toHaveCount(0);
    expect(fixture.active()).toBe(3);

    const refused = await editMember(page, 'Sunil Suspended', { status: 'Active' });
    await expect(refused.locator('.ib-overlay__error')).toContainText(OWNER_FULL);
    await expect(refused.locator('select[formControlName="status"]')).toHaveValue('Active');
    expect(fixture.state.members.find((member) => member.userId === 'member-sunil')?.status).toBe('Suspended');
    await page.screenshot({ path: info.outputPath(`owner-capacity-conflict-${layout}.png`) });
    await refused.getByRole('button', { name: 'Cancel' }).click();
    // The refusal belongs to the cancelled edit: neither the directory nor the next editor shows it.
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.getByText(OWNER_FULL)).toHaveCount(0);
    await page.screenshot({ path: info.outputPath(`owner-refusal-cleared-on-cancel-${layout}.png`) });

    // No one is removed automatically: the owner suspends a member, then retries.
    await expect(await editMember(page, 'Ravi Active', { status: 'Suspended' })).toHaveCount(0);
    await expect(page.getByText(OWNER_FULL)).toHaveCount(0);
    // The same edit, sent again once a seat is free, succeeds.
    await expect(await editMember(page, 'Sunil Suspended', { status: 'Active' })).toHaveCount(0);
    expect(fixture.active()).toBe(3);
    await page.screenshot({ path: info.outputPath(`owner-reinstated-after-suspension-${layout}.png`) });

    const patches = fixture.requests.filter(({ method }) => method === 'PATCH');
    expect(patches).toMatchObject([
      { path: `/api/shops/${SHOP_A}/members/member-asha`, status: 200 },
      { path: `/api/shops/${SHOP_A}/members/member-sunil`, status: 409, code: CAPACITY,
        memberCapacity: { memberAllowance: 3, activeMembers: 3 } },
      { path: `/api/shops/${SHOP_A}/members/member-ravi`, status: 200 },
      { path: `/api/shops/${SHOP_A}/members/member-sunil`, status: 200 },
    ]);
  });
}
