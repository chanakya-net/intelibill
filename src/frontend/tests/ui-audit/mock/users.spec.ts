import {
  expect, test, type Browser, type Locator, type Page, type Route, type TestInfo,
} from '@playwright/test';
import { SUPPORTED_LANGUAGES } from '../../../src/app/core/i18n/language.constants';
import type { ShopUser } from '../../../src/app/features/users/services/user-account.service';
import {
  createShellScenario,
  filterDeclaredShellFailures,
  installShellFixture,
  type DeclaredShellError,
  type ShellApiState,
  type ShellRole,
  type ShellScenario,
} from '../fixtures/shell.fixture';
import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  waitForStablePage,
} from '../support/audit-page';
const API_BASE = 'http://localhost:5277/api';
interface UsersScenarioOptions {
  readonly role?: ShellRole;
  readonly users?: readonly ShopUser[];
  readonly locale?: (typeof SUPPORTED_LANGUAGES)[number];
  readonly longLabels?: boolean;
  readonly apiStates?: Readonly<Partial<Record<'users', ShellApiState>>>;
  readonly declaredErrors?: readonly DeclaredShellError[];
}
interface MutationOptions {
  readonly addFailures?: number;
  readonly editFailures?: number;
  readonly defaultFailures?: number;
}
interface MutationCounts {
  add: number;
  edit: number;
  setDefault: number;
}
test.describe('users', () => {
  test('renders the owner directory with summary counts and user rows', async ({
    page,
  }, testInfo) => {
    const scenario = usersScenario();
    const collector = collectBrowserFailures(page);
    try {
      await openUsers(page, scenario);
      await expect(page.locator('.summary-card')).toHaveCount(4);
      await expect(page.locator('.users-hero__meta')).toContainText('Showing 4 of 4 members');
      await expect(page.locator('.users-hero__meta')).toContainText('Managers: 1');
      await expect(page.locator('.users-hero__meta')).toContainText('Login Enabled: 3');
      if (testInfo.project.name === 'chromium-mobile') {
        await expect(page.locator('.user-card')).toHaveCount(4);
        await expect(page.locator('.mobile-user-list')).toContainText('Legacy Staff');
      } else {
        await expect(page.locator('.desktop-table tbody tr')).toHaveCount(4);
        await expect(page.locator('.desktop-table tbody')).toContainText('Legacy Staff');
      }
      assertFailures(collector.failures, scenario);
    } finally {
      collector.dispose();
    }
  });
  test('applies role-aware affordances and loading/error states', async ({ browser }, testInfo) => {
    for (const role of ['Owner', 'Manager', 'Staff'] as const) {
      const scenario = usersScenario({ role });
      const page = await isolatedPage(browser, testInfo);
      await openUsers(page, scenario);
      await expect(page.locator('.users-hero__actions button:has(.pi-shop)')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Add User' })).toHaveCount(
        role === 'Owner' ? 1 : 0,
      );
      await expect(page.locator('.user-card button:has(.pi-pencil):visible')).toHaveCount(
        role === 'Owner' && testInfo.project.name === 'chromium-mobile' ? 3 : 0,
      );
      await expect(page.locator('.desktop-table th').filter({ hasText: 'Actions' })).toHaveCount(
        role === 'Owner' ? 1 : 0,
      );
      await page.context().close();
    }
    const page = await isolatedPage(browser, testInfo);
    if (testInfo.project.name === 'chromium-mobile') {
      await openUsers(page, usersScenario({ apiStates: { users: 'loading' } }));
      await expect(page.locator('.directory-panel--loading[aria-busy="true"]')).toBeVisible();
    } else {
      const error = { method: 'GET', url: `${API_BASE}/users`, status: 503 } as const;
      await openUsers(page, usersScenario({ declaredErrors: [error] }));
      await expect(page.locator('.error')).toHaveText(
        'Unable to load shop users right now. Please try again.',
      );
    }
    await page.context().close();
  });
  test('filters by search text and role', async ({ page }) => {
    await openUsers(page, usersScenario());
    const search = page.getByPlaceholder('Search users...');
    await search.fill('legacy');
    await expect(visibleUser(page, 'Legacy Staff')).toHaveCount(1);
    await expect(page.locator('.users-hero__meta')).toContainText('Showing 1 of 4 members');
    await search.fill('');
    await page.locator('p-select[inputid="users-role-filter"]').click();
    await page.getByRole('option').filter({ hasText: 'Staff' }).click();
    await expect(page.locator('.user-card:visible, .desktop-table tbody tr:visible')).toHaveCount(2);
    await search.fill('no matching account');
    await expect(page.locator('.empty-state:visible')).toBeVisible();
  });
  test('validates and submits the add user overlay', async ({ page }) => {
    const declaredError = {
      method: 'POST',
      url: `${API_BASE}/users`,
      status: 422,
    } as const;
    const scenario = usersScenario({ declaredErrors: [declaredError] });
    await installShellFixture(page, scenario);
    const counts = await installMutationRoutes(page, scenario, { addFailures: 1 });
    await page.goto('/users');
    await page.getByRole('button', { name: 'Add User' }).click();
    const dialog = page.getByRole('dialog', { name: 'Add user to shop' });
    await dialog.getByRole('button', { name: 'Add User' }).click();
    await expect(dialog.locator('input[formcontrolname="email"]')).toHaveClass(/ng-invalid/);
    await expect(dialog).toContainText('Select at least one shop.');
    expect(counts.add).toBe(0);
    await fillUserForm(dialog, { password: 'password-one', confirm: 'password-two' });
    await dialog.locator('input[type="checkbox"]').first().check();
    await dialog.getByRole('button', { name: 'Add User' }).click();
    await expect(dialog).toContainText('Password and confirm password must match.');
    expect(counts.add).toBe(0);
    await dialog
      .locator('p-password[formcontrolname="confirmPassword"] input')
      .fill('password-one');
    await dialog.getByRole('button', { name: 'Add User' }).click();
    await expect(dialog.locator('.error-message')).toContainText(
      'This email is already used by another account.',
    );
    await dialog.getByRole('button', { name: 'Add User' }).click();
    await expect(dialog).toBeHidden();
    await expect(visibleUser(page, 'Audit Created')).toBeVisible();
    expect(counts.add).toBe(2);
  });
  test('validates and submits the edit user overlay', async ({ page }, testInfo) => {
    const declaredError = {
      method: 'PUT',
      url: `${API_BASE}/users/user-manager`,
      status: 422,
    } as const;
    const scenario = usersScenario({ declaredErrors: [declaredError] });
    await installShellFixture(page, scenario);
    const counts = await installMutationRoutes(page, scenario, { editFailures: 1 });
    await page.goto('/users');
    await visibleUser(page, 'Mira Manager').locator('button:has(.pi-pencil)').click();
    const dialog = page.getByRole('dialog', { name: 'Edit shop user' });
    await expect(dialog.locator('select[formcontrolname="role"]')).toHaveValue('Manager');
    const ownerEdit = visibleUser(page, 'Owner Auditor').locator('button');
    if (testInfo.project.name === 'chromium-mobile') await expect(ownerEdit).toHaveCount(0);
    else await expect(ownerEdit).toBeDisabled();

    for (const checkbox of await dialog.locator('input[type="checkbox"]').all()) {
      if (await checkbox.isChecked()) await checkbox.uncheck();
    }
    await dialog.getByRole('button', { name: 'Save Changes' }).click();
    await expect(dialog).toContainText('Select at least one shop.');
    expect(counts.edit).toBe(0);

    await dialog.locator('input[type="checkbox"]').first().check();
    await dialog.getByRole('button', { name: 'Save Changes' }).click();
    await expect(dialog.locator('.error-message')).toContainText(
      'Owner account cannot be modified from this screen.',
    );
    await dialog.locator('input[formcontrolname="firstName"]').fill('Updated');
    await dialog.getByRole('button', { name: 'Save Changes' }).click();
    await expect(dialog).toBeHidden();
    await expect(visibleUser(page, 'Updated Manager')).toBeVisible();
    expect(counts.edit).toBe(2);
  });
  test('opens and submits the default store overlay', async ({ page }) => {
    const declaredError = {
      method: 'POST',
      url: `${API_BASE}/shops/default`,
      status: 503,
    } as const;
    const scenario = usersScenario({ declaredErrors: [declaredError] });
    await installShellFixture(page, scenario);
    const counts = await installMutationRoutes(page, scenario, { defaultFailures: 1 });
    await page.goto('/users');
    await page.locator('.users-hero__actions button:has(.pi-shop)').click();
    const dialog = page.getByRole('dialog', { name: 'Set Default Store' });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('button', { name: /Primary/ })).not.toHaveClass(
      /p-button-outlined/,
    );

    await dialog.getByRole('button', { name: /Secondary/ }).click();
    await expect(dialog.locator('.server-error')).toHaveText(
      'Unable to set default store right now. Please try again.',
    );
    await dialog.getByRole('button', { name: /Secondary/ }).click();
    await expect(dialog).toBeHidden();
    expect(counts.setDefault).toBe(2);
  });
  test('fits dense data and overlays in every locale and viewport', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'manual locale and viewport matrix');
    test.setTimeout(180_000);
    const scenario = usersScenario({ longLabels: true });
    const dense = usersScenario({ users: denseUsers(scenario), longLabels: true });
    await installShellFixture(page, dense);
    await installMutationRoutes(page, dense);

    for (const locale of SUPPORTED_LANGUAGES) {
      await page.addInitScript((value) => {
        localStorage.setItem('inventory.preferences.language', value);
      }, locale);
      for (const viewport of [{ width: 360, height: 640 }, { width: 1440, height: 900 }]) {
        await page.setViewportSize(viewport);
        await page.goto('/users');
        await expect(page.locator('.users-page')).toBeVisible();
        await assertNoOverflow(page);
        for (const trigger of ['.pi-user-plus', '.pi-pencil', '.pi-shop']) {
          const scope = trigger === '.pi-shop' ? '.users-hero__actions ' : '.users-page ';
          await page.locator(`${scope}button:has(${trigger}):visible:not(:disabled)`).first().click();
          const dialog = page.getByRole('dialog');
          await expect(dialog).toBeVisible();
          await expect(dialog).toHaveAccessibleName(/\S/);
          await assertDialogFits(dialog, viewport);
          await dialog.locator('button:has(.pi-times)').click();
        }
      }
    }
  });
});
function usersScenario(options: UsersScenarioOptions = {}): ShellScenario {
  const base = createShellScenario({
    role: options.role,
    locale: options.locale,
    longLabels: options.longLabels,
    apiStates: options.apiStates,
    declaredErrors: options.declaredErrors,
  });
  return { ...base, users: options.users ?? directoryUsers(base) };
}
function directoryUsers(scenario: ShellScenario): readonly ShopUser[] {
  return [
    {
      userId: scenario.session.user.id,
      firstName: scenario.session.user.firstName,
      lastName: scenario.session.user.lastName,
      email: scenario.session.user.email,
      phoneNumber: scenario.session.user.phoneNumber,
      role: 'Owner',
      isLoginEnabled: true,
      shopIds: scenario.shops.map((shop) => shop.shopId),
    },
    {
      userId: 'user-manager',
      firstName: 'Mira',
      lastName: 'Manager',
      email: 'mira.manager@example.test',
      phoneNumber: '+919876543210',
      role: 'manager',
      isLoginEnabled: true,
      shopIds: scenario.shops.map((shop) => shop.shopId),
    },
    {
      userId: 'user-staff',
      firstName: 'Sam',
      lastName: 'Staff',
      email: 'sam.staff@example.test',
      phoneNumber: '+919876543211',
      role: 'Staff',
      isLoginEnabled: false,
      shopIds: [scenario.shops[0].shopId],
    },
    {
      userId: 'user-legacy',
      firstName: 'Legacy',
      lastName: 'Staff',
      email: null,
      phoneNumber: null,
      role: 'Salesperson',
      isLoginEnabled: true,
      shopIds: [scenario.shops[0].shopId],
    },
  ];
}
async function openUsers(page: Page, scenario: ShellScenario): Promise<void> {
  await installShellFixture(page, scenario);
  await page.goto('/users');
  await waitForStablePage(page);
  await expect(page.locator('.users-page')).toBeVisible();
}
async function installMutationRoutes(
  page: Page,
  scenario: ShellScenario,
  options: MutationOptions = {},
): Promise<MutationCounts> {
  const counts: MutationCounts = { add: 0, edit: 0, setDefault: 0 };
  let addFailures = options.addFailures ?? 0;
  let editFailures = options.editFailures ?? 0;
  let defaultFailures = options.defaultFailures ?? 0;

  await page.route(`${API_BASE}/users`, async (route) => {
    if (route.request().method() !== 'POST') return route.fallback();
    counts.add += 1;
    if (addFailures-- > 0) return mutationError(route, 'Auth.EmailAlreadyInUse');
    const payload = route.request().postDataJSON() as Record<string, unknown>;
    await fulfillJson(route, { ...payload, userId: 'user-created', isLoginEnabled: true });
  });
  await page.route(`${API_BASE}/users/*`, async (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path.startsWith('/api/users/me') || route.request().method() !== 'PUT') {
      return route.fallback();
    }
    counts.edit += 1;
    if (editFailures-- > 0) return mutationError(route, 'Users.CannotModifyOwner');
    const userId = path.split('/').at(-1) ?? '';
    const current = scenario.users.find((user) => user.userId === userId);
    await fulfillJson(route, { ...current, ...route.request().postDataJSON(), userId });
  });
  await page.route(`${API_BASE}/shops/default`, async (route) => {
    if (route.request().method() !== 'POST') return route.fallback();
    counts.setDefault += 1;
    if (defaultFailures-- > 0) return mutationError(route, 'UiAudit.DeclaredError', 503);
    const { shopId } = route.request().postDataJSON() as { shopId: string };
    await fulfillJson(route, {
      ...scenario.session,
      activeShopId: shopId,
      shops: scenario.shops.map((shop) => ({ ...shop, isDefault: shop.shopId === shopId })),
    });
  });
  return counts;
}
async function mutationError(route: Route, title: string, status = 422): Promise<void> {
  await fulfillJson(route, { title }, status);
}
async function fulfillJson(route: Route, body: unknown, status = 200): Promise<void> {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}
function assertFailures(
  failures: Parameters<typeof assertNoUnexpectedBrowserFailures>[0],
  scenario: ShellScenario,
): void {
  assertNoUnexpectedBrowserFailures(filterDeclaredShellFailures(failures, scenario));
}
function visibleUser(page: Page, name: string): Locator {
  return page.locator('.user-card:visible, .desktop-table tbody tr:visible').filter({
    hasText: name,
  });
}
async function isolatedPage(browser: Browser, testInfo: TestInfo): Promise<Page> {
  const context = await browser.newContext({
    baseURL: testInfo.project.use.baseURL as string,
    viewport: testInfo.project.use.viewport,
  });
  return context.newPage();
}
async function fillUserForm(
  dialog: Locator,
  passwords: { readonly password: string; readonly confirm: string },
): Promise<void> {
  await dialog.locator('input[formcontrolname="firstName"]').fill('Audit');
  await dialog.locator('input[formcontrolname="lastName"]').fill('Created');
  await dialog.locator('input[formcontrolname="email"]').fill('audit.created@example.test');
  await dialog.locator('input[formcontrolname="phoneNumber"]').fill('+919876543299');
  await dialog.locator('p-password[formcontrolname="password"] input').fill(passwords.password);
  await dialog.locator('p-password[formcontrolname="confirmPassword"] input').fill(passwords.confirm);
}
function denseUsers(scenario: ShellScenario): readonly ShopUser[] {
  return [
    ...directoryUsers(scenario),
    ...Array.from({ length: 36 }, (_, index) => ({
      userId: `dense-${index}`,
      firstName: `Long-${index}-${'X'.repeat(35)}`,
      lastName: `Account-${'Y'.repeat(35)}`,
      email: `long.user.${index}.${'z'.repeat(30)}@example.test`,
      phoneNumber: index % 5 === 0 ? null : `+91987654${String(index).padStart(4, '0')}`,
      role: index % 2 === 0 ? 'Manager' : 'Staff',
      isLoginEnabled: index % 3 !== 0,
      shopIds: [scenario.shops[0].shopId],
    })),
  ];
}
async function assertNoOverflow(page: Page): Promise<void> {
  const widths = await page.evaluate(() => ({
    document: document.documentElement.scrollWidth,
    viewport: innerWidth,
  }));
  expect(widths.document).toBeLessThanOrEqual(widths.viewport + 1);
}
async function assertDialogFits(
  dialog: Locator,
  viewport: { readonly width: number; readonly height: number },
): Promise<void> {
  const fits = await dialog.locator('.overlay-card').evaluate((card) => {
    const box = card.getBoundingClientRect();
    return box.left >= 0 && box.top >= 0 && box.right <= innerWidth && box.bottom <= innerHeight;
  });
  expect(fits, `${viewport.width}x${viewport.height} dialog bounds`).toBe(true);
}
