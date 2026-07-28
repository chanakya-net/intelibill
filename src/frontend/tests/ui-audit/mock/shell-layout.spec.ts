import { chromium, expect, firefox, test, webkit, type BrowserType, type Page } from '@playwright/test';

import { SUPPORTED_LANGUAGES } from '../../../src/app/core/i18n/language.constants';
import { assertNoUnexpectedBrowserFailures, collectBrowserFailures, waitForStablePage } from '../support/audit-page';
import {
  SHELL_ENGLISH_VIEWPORTS,
  SHELL_LOCALE_VIEWPORTS,
  createShellScenario,
  filterDeclaredShellFailures,
  installShellFixture,
  type ShellScenario,
} from '../fixtures/shell.fixture';

const API_BASE = 'http://localhost:5277/api';
const ROLE_PROFILES = [
  { role: 'Owner', offlineEnabled: true, profileLabels: ['Add Shop', 'Manage Shop', 'Discounts', 'Bank Accounts'], absentLabels: [] },
  { role: 'Manager', offlineEnabled: false, profileLabels: ['Manage Shop', 'Discounts'], absentLabels: ['Add Shop', 'Bank Accounts'] },
  { role: 'Staff', offlineEnabled: false, profileLabels: [], absentLabels: ['Add Shop', 'Manage Shop', 'Discounts', 'Bank Accounts'] },
] as const;
const OVERLAYS = [
  { icon: 'pi-plus-circle', root: '.create-shop-overlay' },
  { icon: 'pi-wrench', root: '.manage-shop-overlay' },
  { icon: 'pi-user-edit', root: '.profile-overlay' },
  { icon: 'pi-key', root: '.profile-overlay' },
] as const;
type OverlayForm = 'none' | 'create' | 'profile' | 'password';

test.describe('shell', () => {
  test('loads deterministic shell fixtures and rejects undeclared failures', async ({ page }) => {
    const scenario = createShellScenario({ role: 'Owner' });
    const collector = collectBrowserFailures(page);

    try {
      await installShellFixture(page, scenario);
      await page.goto('/sales');
      await waitForStablePage(page);

      await expect(page.locator('.app-shell')).toBeVisible();
      await expect(page.locator('.active-shop-caption').first()).toContainText(scenario.shops[0].shopName);
      const declared = { method: 'POST', url: `${API_BASE}/users/me/change-password`, status: 422 } as const;
      const failures = filterDeclaredShellFailures(
        [
          { kind: 'response', message: 'HTTP 422', url: declared.url, method: declared.method },
          { kind: 'response', message: 'HTTP 500', url: declared.url, method: declared.method },
          { kind: 'response', message: 'HTTP 422', url: declared.url, method: 'GET' },
          { kind: 'response', message: 'HTTP 422', url: `${API_BASE}/shops`, method: declared.method },
        ],
        [declared],
      );

      expect(failures).toHaveLength(3);
      expect(failures).toEqual(expect.arrayContaining([
        expect.objectContaining({ message: 'HTTP 500' }),
        expect.objectContaining({ method: 'GET' }),
        expect.objectContaining({ url: `${API_BASE}/shops` }),
      ]));
      assertNoUnexpectedBrowserFailures(filterDeclaredShellFailures(collector.failures, scenario));
    } finally {
      collector.dispose();
    }
  });

  test('keeps responsive navigation and long labels inside every English viewport', async ({}, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'manual scoped browser matrix');
    test.setTimeout(180_000);

    for (const browserType of [chromium, firefox, webkit]) {
      for (const viewport of SHELL_ENGLISH_VIEWPORTS) {
        await withShellPage(
          browserType,
          { width: viewport.width, height: viewport.height },
          createShellScenario({ role: 'Owner', longLabels: true }),
          (page) => assertResponsiveShell(page, viewport.width),
        );
      }
    }

    for (const locale of SUPPORTED_LANGUAGES) {
      for (const viewport of SHELL_LOCALE_VIEWPORTS) {
        await withShellPage(
          chromium,
          { width: viewport.width, height: viewport.height },
          createShellScenario({ locale, longLabels: true }),
          (page) => assertResponsiveShell(page, viewport.width),
        );
      }
    }
  });

  test('renders role-aware shop, profile, and feature controls', async ({}, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'representative role profiles');
    test.setTimeout(90_000);

    for (const profile of ROLE_PROFILES) {
      await withShellPage(
        chromium,
        { width: 1440, height: 900 },
        createShellScenario({ role: profile.role, offlineEnabled: profile.offlineEnabled, longLabels: true }),
        (page) => assertRoleControls(page, profile),
      );
    }

    await withShellPage(
      chromium,
      { width: 360, height: 800 },
      createShellScenario({ role: 'Owner', offlineEnabled: false, apiStates: { offlineSnapshot: 'loading' } }),
      async (page) => {
        const toggle = page.locator('.offline-sales-toggle');
        await toggle.click();
        await expect(toggle).toHaveClass(/is-pending/);
        await expect(toggle).toBeDisabled();
      },
    );
  });

  test('fits and stacks every shell overlay in open, loading, and error states', async ({}, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'representative overlay profiles');
    test.setTimeout(90_000);

    for (const overlay of OVERLAYS) {
      await withShellPage(
        chromium,
        { width: 360, height: 640 },
        createShellScenario({ role: 'Owner', locale: 'ml-IN', longLabels: true }),
        async (page) => {
          const drawerZIndex = await openMobileOverlay(page, overlay.icon);
          await assertOverlayFit(page, overlay.root, drawerZIndex);
        },
      );
    }

    await withShellPage(
      chromium,
      { width: 360, height: 640 },
      createShellScenario({ role: 'Owner', apiStates: { shopDetails: 'loading' } }),
      async (page) => {
        const drawerZIndex = await openMobileOverlay(page, 'pi-wrench');
        await assertOverlayFit(page, '.manage-shop-overlay', drawerZIndex);
        await expect(page.locator('.manage-shop-overlay .submit-overlay')).toBeVisible();
      },
    );

    for (const errorCase of [
      overlayError('pi-wrench', '.manage-shop-overlay', 'GET', 'shops/shop-primary', 503, 'Shop.ShopNotFound', 'none'),
      overlayError('pi-plus-circle', '.create-shop-overlay', 'POST', 'shops', 422, 'Shop.NameRequired', 'create'),
      overlayError('pi-user-edit', '.profile-overlay', 'PUT', 'users/me', 422, 'User.EmailAlreadyExists', 'profile'),
      overlayError('pi-key', '.profile-overlay', 'POST', 'users/me/change-password', 422, 'User.CurrentPasswordInvalid', 'password'),
    ]) {
      await withShellPage(
        chromium,
        { width: 360, height: 640 },
        createShellScenario({ role: 'Owner', declaredErrors: [errorCase.error] }),
        async (page) => {
          const drawerZIndex = await openMobileOverlay(page, errorCase.icon);
          await submitOverlayForm(page, errorCase.form);
          await expect(page.locator(`${errorCase.root} .server-error`)).toBeVisible();
          await assertOverlayFit(page, errorCase.root, drawerZIndex);
        },
      );
    }
  });
});

async function withShellPage(browserType: BrowserType, viewport: { readonly width: number; readonly height: number }, scenario: ShellScenario, assertion: (page: Page) => Promise<void>): Promise<void> {
  const browser = await browserType.launch();
  const context = await browser.newContext({
    viewport,
    locale: scenario.locale,
    timezoneId: 'Asia/Kolkata',
    colorScheme: 'light',
    reducedMotion: 'reduce',
    serviceWorkers: 'block',
    baseURL: 'http://127.0.0.1:4300',
  });
  const page = await context.newPage();
  const collector = collectBrowserFailures(page);

  try {
    await installShellFixture(page, scenario);
    await page.goto('/sales');
    await waitForStablePage(page);
    await assertion(page);
    assertNoUnexpectedBrowserFailures(
      filterDeclaredShellFailures(collector.failures, scenario),
    );
  } finally {
    collector.dispose();
    await context.close();
    await browser.close();
  }
}

async function assertResponsiveShell(page: Page, viewportWidth: number): Promise<void> {
  const sidebar = page.locator('.shell-sidebar');
  const mobileTrigger = page.locator('.mobile-menu-trigger');
  const sidebarVisible = await sidebar.isVisible();
  const mobileTriggerVisible = await mobileTrigger.isVisible();

  expect(Number(sidebarVisible) + Number(mobileTriggerVisible)).toBe(1);
  expect(sidebarVisible).toBe(viewportWidth > 768);
  expect(mobileTriggerVisible).toBe(viewportWidth <= 768);

  const bounds = await page.evaluate(() => {
    const insideViewport = (element: Element | null) => {
      const box = element?.getBoundingClientRect();
      return !!box && box.left >= 0 && box.right <= innerWidth && box.top >= 0;
    };
    const caption = document.querySelector<HTMLElement>('.active-shop-caption');
    const captionStyle = caption ? getComputedStyle(caption) : null;

    return {
      documentWidth: document.documentElement.scrollWidth,
      shellInside: insideViewport(document.querySelector('.app-shell')),
      headerInside: insideViewport(document.querySelector('.shell-header')),
      sidebarInside: insideViewport(document.querySelector('.shell-sidebar')),
      captionInside: insideViewport(caption),
      captionTruncated:
        !!caption &&
        (caption.scrollWidth <= caption.clientWidth + 1 ||
          captionStyle?.textOverflow === 'ellipsis' ||
          captionStyle?.whiteSpace === 'normal'),
    };
  });

  expect(bounds.documentWidth).toBeLessThanOrEqual(viewportWidth);
  expect(bounds.shellInside).toBe(true);
  expect(bounds.headerInside).toBe(true);
  expect(bounds.captionInside).toBe(true);
  expect(bounds.captionTruncated).toBe(true);
  if (sidebarVisible) {
    expect(bounds.sidebarInside).toBe(true);
    await expect(page.locator('.shell-panelmenu').first()).toBeVisible();
    return;
  }

  await mobileTrigger.click();
  const drawer = page.locator('.mobile-nav-drawer');
  await expect(drawer).toBeVisible();
  const drawerMetrics = await drawer.evaluate((element) => {
    const box = element.getBoundingClientRect();
    const mask = element.closest<HTMLElement>('.p-drawer-mask');
    return {
      left: box.left,
      right: box.right,
      top: box.top,
      bottom: box.bottom,
      scrollWidth: element.scrollWidth,
      clientWidth: element.clientWidth,
      zIndex: Number.parseInt(getComputedStyle(mask ?? element).zIndex || '0', 10),
    };
  });
  expect(drawerMetrics.left).toBeGreaterThanOrEqual(0);
  expect(drawerMetrics.right).toBeLessThanOrEqual(viewportWidth);
  expect(drawerMetrics.top).toBeGreaterThanOrEqual(0);
  expect(drawerMetrics.scrollWidth).toBeLessThanOrEqual(drawerMetrics.clientWidth);
  expect(drawerMetrics.zIndex).toBeGreaterThan(30);
}

async function assertRoleControls(page: Page, profile: (typeof ROLE_PROFILES)[number]): Promise<void> {
  const profileHeader = page.locator(
    '.shell-sidebar .shell-panelmenu-profile .p-panelmenu-header-link',
  );
  await profileHeader.click();
  const profileMenu = page.locator('.shell-sidebar .shell-panelmenu-profile');
  for (const label of ['Manage Users', 'Update Profile', 'Change Password', 'Language', 'Logout']) {
    await expect(profileMenu.getByText(label, { exact: true })).toBeVisible();
  }
  for (const label of profile.profileLabels) {
    await expect(profileMenu.getByText(label, { exact: true })).toBeVisible();
  }
  for (const label of profile.absentLabels) {
    await expect(profileMenu.getByText(label, { exact: true })).toHaveCount(0);
  }

  const featureToggle = page.locator('.offline-sales-toggle');
  if (profile.role === 'Staff') {
    await expect(featureToggle).toHaveCount(0);
  } else {
    await expect(featureToggle).toHaveAttribute(
      'aria-pressed',
      profile.offlineEnabled ? 'true' : 'false',
    );
  }

  const shopTrigger = page.locator('button[aria-label="Switch active shop"]:visible');
  await shopTrigger.click();
  const shopMenu = page.locator('.shop-menu');
  await expect(shopMenu).toBeVisible();
  const menuBounds = await shopMenu.evaluate((element) => {
    const box = element.getBoundingClientRect();
    return {
      left: box.left,
      right: box.right,
      top: box.top,
      bottom: box.bottom,
      zIndex: Number.parseInt(getComputedStyle(element).zIndex, 10),
    };
  });
  expect(menuBounds.left).toBeGreaterThanOrEqual(0);
  expect(menuBounds.right).toBeLessThanOrEqual(1440);
  expect(menuBounds.top).toBeGreaterThanOrEqual(0);
  expect(menuBounds.bottom).toBeLessThanOrEqual(900);
  expect(menuBounds.zIndex).toBeGreaterThan(30);

  await page.locator('.shell-main').click({ position: { x: 1, y: 1 } });
  await expect(shopMenu).toHaveCount(0);
  await shopTrigger.click();
  const switchRequest = page.waitForRequest(
    (request) =>
      request.method() === 'POST' &&
      request.url() === `${API_BASE}/shops/default`,
  );
  await shopMenu.getByRole('menuitem').filter({ hasText: 'Secondary' }).click();
  await switchRequest;
  await expect(shopMenu).toHaveCount(0);
}

async function assertOverlayFit(page: Page, rootSelector: string, coveredZIndex: number): Promise<void> {
  const root = page.locator(rootSelector);
  const card = root.locator('.overlay-card');
  await expect(root).toBeVisible();
  await expect(card).toBeVisible();
  await expect(root.locator('.overlay-close')).toBeVisible();

  const metrics = await card.evaluate((element) => {
    const box = element.getBoundingClientRect();
    const scrollBody = element.querySelector<HTMLElement>('.overlay-form');
    return {
      left: box.left,
      right: box.right,
      top: box.top,
      bottom: box.bottom,
      rootZIndex: Number.parseInt(
        getComputedStyle(element.closest<HTMLElement>('[role="dialog"]') ?? element).zIndex,
        10,
      ),
      scrollOverflow: scrollBody ? getComputedStyle(scrollBody).overflowY : '',
    };
  });
  expect(metrics.left).toBeGreaterThanOrEqual(0);
  expect(metrics.right).toBeLessThanOrEqual(360);
  expect(metrics.top).toBeGreaterThanOrEqual(0);
  expect(metrics.bottom).toBeLessThanOrEqual(640);
  expect(metrics.rootZIndex).toBeGreaterThan(coveredZIndex);
  expect(['auto', 'scroll']).toContain(metrics.scrollOverflow);

  const lastAction = card.locator('button').last();
  await lastAction.scrollIntoViewIfNeeded();
  await expect(lastAction).toBeVisible();
}

async function openMobileOverlay(page: Page, icon: string): Promise<number> {
  await page.locator('.mobile-menu-trigger').click();
  const drawer = page.locator('.mobile-nav-drawer');
  await expect(drawer).toBeVisible();
  const zIndex = await drawer.evaluate((element) =>
    Number.parseInt(
      getComputedStyle(element.closest<HTMLElement>('.p-drawer-mask') ?? element).zIndex,
      10,
    ),
  );
  await drawer.locator('.mobile-profile-section .p-panelmenu-header-link').click();
  await drawer
    .locator(`.mobile-profile-section .p-panelmenu-item-link:has(.${icon})`)
    .click();
  return zIndex;
}

async function submitOverlayForm(page: Page, form: OverlayForm): Promise<void> {
  if (form === 'none') return;
  if (form === 'create') {
    await page.locator('#shop-name').fill('Audit Error Shop');
    await page.locator('#shop-address').fill('123 Audit Avenue');
    await page.locator('#shop-city').fill('Bengaluru');
    await page.locator('#shop-state').fill('Karnataka');
    await page.locator('#shop-pincode').fill('560001');
    await page.locator('.create-shop-overlay button[type="submit"]').click();
    return;
  }
  if (form === 'profile') {
    await page.locator('.profile-overlay button[type="submit"]').click();
    return;
  }
  await page.locator('#current-password').fill('CurrentPassword1!');
  await page.locator('#new-password').fill('NewPassword1!');
  await page.locator('#confirm-new-password').fill('NewPassword1!');
  await page.locator('.profile-overlay button[type="submit"]').click();
}

function overlayError(icon: string, root: string, method: string, path: string, status: number, title: string, form: OverlayForm) {
  return {
    icon,
    root,
    error: { method, url: `${API_BASE}/${path}`, status, body: { title } },
    form,
  };
}
