import { expect, type Locator, type Page, type Route } from '@playwright/test';

import type { Service } from '../../../src/app/features/services/services/service.models';
import { mockExternalRequests, waitForStablePage } from '../support/audit-page';

export const SERVICES: readonly Service[] = [
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

export interface ServiceScenarioOptions {
  readonly role?: 'Owner' | 'Staff';
  readonly listState?: 'ready' | 'error';
  readonly failFirstMutation?: boolean;
  readonly services?: readonly Service[];
  readonly locale?: string;
}

const UNBROKEN_TOKEN = 'Extended'.repeat(12);
export const FIRST_PAGE_LONG_MARKER = 'LongFirstPageService';
export const SECOND_PAGE_LONG_MARKER = 'LongSecondPageService';

/** Boundary-length values (180-character name, 320-character description) with an unbroken run. */
function longServiceName(marker: string): string {
  return `${marker}-${UNBROKEN_TOKEN}`.padEnd(180, 'N').slice(0, 180);
}

function longServiceDescription(marker: string): string {
  return `${marker} description ${UNBROKEN_TOKEN}`.padEnd(320, 'D').slice(0, 320);
}

const LONG_VALUE_INDEXES = new Map([
  [0, FIRST_PAGE_LONG_MARKER],
  [21, SECOND_PAGE_LONG_MARKER],
]);

export const DENSE_SERVICES: readonly Service[] = Array.from({ length: 23 }, (_, index) => {
  const longMarker = LONG_VALUE_INDEXES.get(index);
  return {
    ...SERVICES[index % SERVICES.length],
    serviceId: `dense-service-${index + 1}`,
    code: `SRV-${String(index + 1).padStart(4, '0')}`,
    name: longMarker ? longServiceName(longMarker) : `Audit service ${index + 1}`,
    description: longMarker
      ? longServiceDescription(longMarker)
      : `Service description ${index + 1}.`,
  };
});

export async function openServices(
  page: Page,
  options: ServiceScenarioOptions = {},
): Promise<void> {
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

export function visibleServices(page: Page): Locator {
  return page.locator('.service-card:visible, .desktop-table tbody tr:visible');
}

export async function fillServiceForm(
  dialog: Locator,
  values: { name: string; price: string },
): Promise<void> {
  await dialog.locator('input[formcontrolname="name"]').fill(values.name);
  await dialog.locator('input[id$="-service-price"]').fill(values.price);
  await dialog.locator('input[formcontrolname="hsnCode"]').fill('9987');
  await dialog.locator('input[id$="-service-tax"]').fill('18');
}

export async function submitInvalidServiceForm(dialog: Locator): Promise<void> {
  await dialog.locator('input[formcontrolname="name"]').fill('N'.repeat(181));
  await dialog.locator('textarea[formcontrolname="description"]').fill('D'.repeat(321));
  await dialog.locator('input[id$="-service-price"]').fill('');
  await dialog.locator('input[id$="-service-tax"]').fill('');
  await dialog.locator('input[formcontrolname="hsnCode"]').fill('ABC');
  await dialog.getByRole('button', { name: /Add Service|Save Changes/ }).click();
}

/**
 * Long values must wrap inside their own cell or card instead of being clipped or
 * pushed past the container edge, in both the desktop table and the mobile cards.
 */
export async function assertServiceValueFitsContainer(page: Page, marker: string): Promise<void> {
  const row = visibleServices(page).filter({ hasText: marker }).first();
  await expect(row).toBeVisible();

  const bounds = await row.evaluate((element) => {
    const measure = (selector: string) => {
      const node = element.querySelector<HTMLElement>(selector);
      const container = node?.closest<HTMLElement>('td, .service-card');
      if (!node || !container) return null;
      return {
        clipped: node.scrollWidth - node.clientWidth,
        overflowRight: node.getBoundingClientRect().right - container.getBoundingClientRect().right,
      };
    };
    return { name: measure('.service-name'), description: measure('.service-description') };
  });

  for (const measurement of [bounds.name, bounds.description]) {
    expect(measurement).not.toBeNull();
    expect(measurement!.clipped).toBeLessThanOrEqual(1);
    expect(measurement!.overflowRight).toBeLessThanOrEqual(1);
  }
}

export async function assertNoHorizontalOverflow(page: Page): Promise<void> {
  const dimensions = await page.evaluate(() => ({
    body: document.body.scrollWidth,
    document: document.documentElement.scrollWidth,
    viewport: window.innerWidth,
  }));
  expect(dimensions.body).toBeLessThanOrEqual(dimensions.viewport + 5);
  expect(dimensions.document).toBeLessThanOrEqual(dimensions.viewport + 5);
}

/** Hindi strings the services surfaces must render instead of English or raw keys. */
export const HINDI_SERVICE_LABELS = {
  code: 'कोड',
  name: 'नाम',
  price: 'कीमत',
  taxRatePercent: 'कर दर %',
  hsnSac: 'HSN/SAC',
  status: 'स्थिति',
  actions: 'क्रियाएं',
  all: 'सभी',
  active: 'सक्रिय',
  inactive: 'निष्क्रिय',
  taxIncluded: 'कर शामिल',
  edit: 'संपादित करें',
  deactivate: 'निष्क्रिय करें',
  cancel: 'रद्द करें',
  editService: 'सेवा संपादित करें',
  saveChanges: 'परिवर्तन सहेजें',
} as const;

export async function assertLocalizedStatusFilterOptions(page: Page): Promise<void> {
  const { all, active, inactive, status } = HINDI_SERVICE_LABELS;
  await expect(page.getByRole('combobox', { name: status, exact: true })).toBeVisible();
  await page.locator('p-select[inputid="services-status-filter"]').click();
  for (const label of [all, active, inactive]) {
    await expect(page.getByRole('option', { name: label, exact: true })).toBeVisible();
  }
  await page.keyboard.press('Escape');
  await expect(page.getByRole('option', { name: all, exact: true })).toBeHidden();
}

export async function assertLocalizedServiceList(page: Page, projectName: string): Promise<void> {
  const labels = HINDI_SERVICE_LABELS;
  const row = visibleServices(page).filter({ hasText: 'Installation' }).first();

  if (projectName === 'chromium-mobile') {
    await expect(row.locator('dt')).toHaveText([
      labels.price,
      labels.taxRatePercent,
      labels.hsnSac,
    ]);
  } else {
    await expect(page.locator('.desktop-table thead th')).toHaveText([
      labels.code,
      labels.name,
      labels.price,
      labels.taxRatePercent,
      labels.hsnSac,
      labels.status,
      labels.actions,
    ]);
    await expect(row.locator('p-tag').first()).toHaveText(labels.taxIncluded);
  }

  await expect(row.locator('p-tag').last()).toHaveText(labels.active);
  await expect(row.getByRole('button', { name: labels.edit, exact: true })).toBeVisible();
  await expect(row.getByRole('button', { name: labels.deactivate, exact: true })).toBeVisible();
  await expect(
    visibleServices(page).filter({ hasText: 'Annual support' }).first().locator('p-tag').last(),
  ).toHaveText(labels.inactive);
}

export async function assertLocalizedEditOverlay(page: Page): Promise<void> {
  const labels = HINDI_SERVICE_LABELS;
  await visibleServices(page).filter({ hasText: 'Installation' }).locator('button').first().click();
  const dialog = page.locator('.service-editor-dialog');
  await expect(dialog.locator('h2')).toHaveText(labels.editService);
  await expect(dialog.locator('label[for="edit-service-name"]')).toHaveText(labels.name);
  await expect(dialog.locator('label[for="edit-service-price"]')).toHaveText(labels.price);
  await expect(dialog.locator('label[for="edit-service-tax"]')).toHaveText(labels.taxRatePercent);
  await expect(dialog.getByRole('button', { name: labels.saveChanges, exact: true })).toBeVisible();
}
