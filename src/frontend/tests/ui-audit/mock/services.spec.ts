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
  readonly locale?: string;
}

const DENSE_SERVICES: readonly Service[] = Array.from({ length: 23 }, (_, index) => ({
  ...SERVICES[index % SERVICES.length],
  serviceId: `dense-service-${index + 1}`,
  code: `SRV-${String(index + 1).padStart(4, '0')}`,
  name: `Audit service ${index + 1}`,
  description:
    index === 20
      ? 'Long service description retained after pagination to verify responsive wrapping in every layout.'
      : `Service description ${index + 1}.`,
}));

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

  test('explains every blocking add and edit validation error', async ({ page }) => {
    await openServices(page);
    await page.getByRole('button', { name: 'Add Service' }).click();
    const addDialog = page.locator('.service-editor-dialog');
    await submitInvalidServiceForm(addDialog);
    await expect(addDialog.locator('#add-service-name-error')).toHaveText(
      'Service name must be 180 characters or fewer.',
    );
    await expect(addDialog.locator('#add-service-description-error')).toHaveText(
      'Description must be 320 characters or fewer.',
    );
    await expect(addDialog.locator('#add-service-price-error')).toHaveText(
      'This field is required',
    );
    await expect(addDialog.locator('#add-service-tax-error')).toHaveText('This field is required');
    await expect(addDialog.locator('#add-service-hsn-error')).toHaveText(
      'Enter a valid 4 to 8 digit HSN/SAC code.',
    );
    await expect(addDialog.locator('input[formcontrolname="name"]')).toHaveAttribute(
      'aria-describedby',
      'add-service-name-error',
    );
    await expect(addDialog.locator('input#add-service-price')).toHaveAttribute(
      'aria-describedby',
      'add-service-price-error',
    );

    await addDialog.getByRole('button', { name: 'Cancel' }).click();
    await visibleServices(page)
      .filter({ hasText: 'Installation' })
      .locator('button')
      .first()
      .click();
    const editDialog = page.locator('.service-editor-dialog');
    await submitInvalidServiceForm(editDialog);
    await expect(editDialog.locator('#edit-service-name-error')).toHaveText(
      'Service name must be 180 characters or fewer.',
    );
    await expect(editDialog.locator('input#edit-service-tax')).toHaveAttribute(
      'aria-describedby',
      'edit-service-tax-error',
    );
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
      await expect(page.locator('.empty-state')).toHaveCount(0);

      await openServices(page, { services: [] });
      await expect(page.locator('.empty-state:visible')).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('keeps the route unavailable to staff users', async ({ page }) => {
    await openServices(page, { role: 'Staff' });
    await expect(page).toHaveURL(/\/(dashboard|sales)$/);
  });

  test('paginates dense long service values within every viewport', async ({ page }) => {
    await openServices(page, { services: DENSE_SERVICES });
    await expect(visibleServices(page)).toHaveCount(20);
    await expect(visibleServices(page).filter({ hasText: 'Audit service 20' })).toBeVisible();
    await page.locator('.p-paginator-next').click();
    await expect(visibleServices(page)).toHaveCount(3);
    await expect(visibleServices(page).filter({ hasText: 'Audit service 21' })).toBeVisible();
    await assertNoHorizontalOverflow(page);
  });

  test('localizes services controls, list, and overlays', async ({ page }) => {
    await openServices(page, { locale: 'hi-IN' });
    await expect(page.locator('.services-page h1')).toHaveText('सेवाएँ');
    await expect(page.locator('input[placeholder="सेवाएँ खोजें..."]')).toBeVisible();
    await expect(page.locator('.table-caption')).toHaveText('1 - 2 में से 2');
    await expect(visibleServices(page).filter({ hasText: 'Installation' })).toBeVisible();
    await page.getByRole('button', { name: 'सेवा जोड़ें' }).click();
    await expect(page.locator('.service-editor-dialog h2')).toHaveText('सेवा जोड़ें');
    await expect(page.locator('.service-editor-dialog label[for="add-service-name"]')).toHaveText(
      'नाम',
    );
  });
});

async function openServices(page: Page, options: ServiceScenarioOptions = {}): Promise<void> {
  await mockExternalRequests(page, { authenticated: true, locale: options.locale });
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

async function submitInvalidServiceForm(dialog: ReturnType<Page['getByRole']>): Promise<void> {
  await dialog.locator('input[formcontrolname="name"]').fill('N'.repeat(181));
  await dialog.locator('textarea[formcontrolname="description"]').fill('D'.repeat(321));
  await dialog.locator('input[id$="-service-price"]').fill('');
  await dialog.locator('input[id$="-service-tax"]').fill('');
  await dialog.locator('input[formcontrolname="hsnCode"]').fill('ABC');
  await dialog.getByRole('button', { name: /Add Service|Save Changes/ }).click();
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
