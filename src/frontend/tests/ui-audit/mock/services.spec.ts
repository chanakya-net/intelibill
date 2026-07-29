import { expect, test, type Page, type Route } from '@playwright/test';

import type { Service } from '../../../src/app/features/services/services/service.models';
import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  mockExternalRequests,
  waitForStablePage,
} from '../support/audit-page';

const SERVICES: readonly Service[] = [
  {
    serviceId: 'service-installation',
    code: 'SRV-0001',
    name: 'Installation',
    description: 'On-site installation and setup.',
    price: 1250,
    hsnCode: '9987',
    taxRatePercent: 18,
    taxIncluded: true,
    isActive: true,
  },
  {
    serviceId: 'service-support',
    code: 'SRV-0002',
    name: 'Annual support',
    description: 'Remote and on-site support.',
    price: 3500,
    hsnCode: '9983',
    taxRatePercent: 18,
    taxIncluded: false,
    isActive: false,
  },
];

interface ServiceScenarioOptions {
  readonly role?: 'Owner' | 'Staff';
  readonly listState?: 'ready' | 'error';
  readonly failFirstMutation?: boolean;
  readonly services?: readonly Service[];
}

test.describe('services', () => {
  test('renders service data as a table on desktop and cards on mobile', async ({
    page,
  }, testInfo) => {
    const collector = collectBrowserFailures(page);
    try {
      await openServices(page);

      if (testInfo.project.name === 'chromium-mobile') {
        await expect(page.locator('.service-card')).toHaveCount(SERVICES.length);
        await expect(page.locator('.service-card').first()).toContainText('Installation');
      } else {
        await expect(page.locator('p-table')).toBeVisible();
        await expect(page.locator('tbody tr')).toHaveCount(SERVICES.length);
        await expect(page.locator('tbody')).toContainText('Annual support');
      }

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('validates and submits add and edit service overlays', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openServices(page);
      await page.getByRole('button', { name: 'Add Service' }).click();
      const addDialog = page.locator('.service-editor-dialog');
      await addDialog.getByRole('button', { name: 'Add Service' }).click();
      await expect(addDialog.locator('.field-error')).toHaveCount(1);
      await expect(addDialog.locator('input[formcontrolname="name"]')).toHaveAttribute(
        'aria-invalid',
        'true',
      );

      await fillServiceForm(addDialog, { name: 'Created audit service', price: '640' });
      await addDialog.getByRole('button', { name: 'Add Service' }).click();
      await expect(addDialog).toBeHidden();
      await expect(
        visibleServices(page).filter({ hasText: 'Created audit service' }),
      ).toBeVisible();

      await visibleServices(page)
        .filter({ hasText: 'Installation' })
        .locator('button')
        .first()
        .click();
      const editDialog = page.locator('.service-editor-dialog');
      await expect(editDialog.locator('input[formcontrolname="name"]')).toHaveValue('Installation');
      await fillServiceForm(editDialog, { name: 'Updated installation', price: '1450' });
      await editDialog.getByRole('button', { name: 'Save Changes' }).click();
      await expect(editDialog).toBeHidden();
      await expect(visibleServices(page).filter({ hasText: 'Updated installation' })).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('filters services and reports list and save errors', async ({ page }) => {
    const collector = collectBrowserFailures(page, {
      ignoreConsole: (message) =>
        message.includes('503') && message.includes('Failed to load resource'),
      ignoreResponse: (response) =>
        response.url().includes('/api/services') && response.status() === 503,
    });
    try {
      await openServices(page);
      const search = page.getByPlaceholder('Search services...');
      await search.fill('support');
      await expect(visibleServices(page)).toHaveCount(1);
      await expect(visibleServices(page)).toContainText('Annual support');
      await search.fill('');
      await page.locator('p-select[inputid="services-status-filter"]').click();
      await page.getByRole('option', { name: 'Inactive' }).click();
      await expect(visibleServices(page)).toHaveCount(1);
      await expect(visibleServices(page)).toContainText('Annual support');

      await openServices(page, { failFirstMutation: true });
      await page.getByRole('button', { name: 'Add Service' }).click();
      const dialog = page.locator('.service-editor-dialog');
      await fillServiceForm(dialog, { name: 'Unavailable service', price: '640' });
      await dialog.getByRole('button', { name: 'Add Service' }).click();
      await expect(dialog.locator('.error-banner')).toHaveText('Unable to save service.');

      await openServices(page, { listState: 'error' });
      await expect(page.locator('.error')).toHaveText('Unable to load services.');
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('keeps the route unavailable to staff users', async ({ page }) => {
    await openServices(page, { role: 'Staff' });
    await expect(page).toHaveURL(/\/(dashboard|sales)$/);
  });

  test('keeps dense service values inside the mobile viewport', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'mobile layout coverage');
    const longService: Service = {
      ...SERVICES[0],
      code: 'SRV-VERY-LONG-SERVICE-CODE-THAT-MUST-NOT-ESCAPE-THE-CARD-BOUNDARY-0001',
      name: 'Installation and commissioning for complex multi-location enterprise equipment',
      description: 'A deliberately long service description used to verify responsive wrapping.',
    };
    await openServices(page, { services: [longService] });
    await expect(page.locator('.service-card')).toContainText(longService.name);
    await assertNoHorizontalOverflow(page);
  });
});

async function openServices(page: Page, options: ServiceScenarioOptions = {}): Promise<void> {
  await mockExternalRequests(page, { authenticated: true });
  if (options.role === 'Staff') {
    await page.addInitScript(() => {
      const key = 'inventory.auth.session.local';
      const session = JSON.parse(localStorage.getItem(key) ?? '{}') as {
        shops?: { role: string }[];
      };
      if (session.shops) session.shops = session.shops.map((shop) => ({ ...shop, role: 'Staff' }));
      localStorage.setItem(key, JSON.stringify(session));
    });
  }
  let services = [...(options.services ?? SERVICES)];
  let failNextMutation = options.failFirstMutation ?? false;
  await page.route('**/api/services**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (request.method() === 'GET') {
      if (options.listState === 'error') {
        await route.fulfill({ status: 503, contentType: 'application/json', body: '{}' });
        return;
      }
      const search = url.searchParams.get('search')?.toLowerCase() ?? '';
      await fulfillServices(
        route,
        services.filter((service) => service.name.toLowerCase().includes(search)),
      );
      return;
    }

    if (request.method() === 'POST' && url.pathname === '/api/services') {
      if (failNextMutation) {
        failNextMutation = false;
        await route.fulfill({ status: 503, contentType: 'application/json', body: '{}' });
        return;
      }
      const payload = request.postDataJSON() as Omit<Service, 'serviceId' | 'code'>;
      const service: Service = {
        ...payload,
        serviceId: `service-${services.length + 1}`,
        code: `SRV-${String(services.length + 1).padStart(4, '0')}`,
      };
      services = [...services, service];
      await fulfillServices(route, service);
      return;
    }

    const serviceId = url.pathname.match(/^\/api\/services\/([^/]+)$/)?.[1];
    if (request.method() === 'PATCH' && serviceId) {
      const payload = request.postDataJSON() as Partial<Service>;
      services = services.map((service) =>
        service.serviceId === serviceId ? { ...service, ...payload } : service,
      );
      await route.fulfill({ status: 200, contentType: 'application/json', body: '{}' });
      return;
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: '{}' });
  });
  await page.route('**/api/hsn/lookup', async (route) => {
    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({ hsnCodes: ['9987'], taxScenarios: [{ taxPercentage: '18%' }] }),
    });
  });
  await page.goto('/services');
  await waitForStablePage(page);
  if (options.role === 'Staff') return;
  await expect(page.locator('.services-page')).toBeVisible();
}

async function fulfillServices(route: Route, body: unknown): Promise<void> {
  await route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
}

function visibleServices(page: Page) {
  return page.locator('.service-card:visible, .desktop-table tbody tr:visible');
}

async function fillServiceForm(
  dialog: ReturnType<Page['getByRole']>,
  values: { name: string; price: string },
): Promise<void> {
  await dialog.locator('input[formcontrolname="name"]').fill(values.name);
  await dialog.locator('input[id$="-service-price"]').fill(values.price);
  await dialog.locator('input[formcontrolname="hsnCode"]').fill('9987');
  await dialog.locator('input[id$="-service-tax"]').fill('18');
}

async function assertNoHorizontalOverflow(page: Page): Promise<void> {
  const dimensions = await page.evaluate(() => ({
    body: document.body.scrollWidth,
    document: document.documentElement.scrollWidth,
    viewport: window.innerWidth,
  }));
  expect(dimensions.body).toBeLessThanOrEqual(dimensions.viewport + 5);
  expect(dimensions.document).toBeLessThanOrEqual(dimensions.viewport + 5);
}
