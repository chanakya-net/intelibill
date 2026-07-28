import {
  chromium,
  expect,
  firefox,
  test,
  webkit,
  type BrowserType,
  type Page,
  type Route,
} from '@playwright/test';

import type {
  CreditNoteListItemDto,
  CreditNoteStatus,
} from '../../../src/app/features/sales/services/sale.models';
import { SUPPORTED_LANGUAGES } from '../../../src/app/core/i18n/language.constants';
import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  waitForStablePage,
} from '../support/audit-page';
import { createShellScenario, installShellFixture } from '../fixtures/shell.fixture';

const API_BASE = 'http://localhost:5277/api';
const ROUTE = '/sales/credit-notes';
const VIEWPORTS = [
  { width: 360, height: 800 },
  { width: 768, height: 1024 },
  { width: 1440, height: 900 },
] as const;

const STATUS_NOTES = [
  note('active', 'Active'),
  note('redeemed', 'FullyRedeemed', { availableBalance: 0 }),
  note('expired', 'Expired'),
  note('voided', 'Voided', { availableBalance: 0 }),
] as const;

const LONG_NOTE = note('long', 'Active', {
  code: 'CN-2026-LONG-DETERMINISTIC-CUSTOMER-REFERENCE-000001',
  customerName: 'Alexandria Cassandra Long Customer Name For Responsive Audit',
  invoiceNumber: 'INV-2026-LONG-INVOICE-REFERENCE-000001',
  returnNumber: 'RET-2026-LONG-RETURN-REFERENCE-000001',
});

const DENSE_NOTES = Array.from({ length: 24 }, (_, index) =>
  note(`dense-${index + 1}`, index % 2 === 0 ? 'Active' : 'Expired', {
    code: `CN-2026-${String(index + 1).padStart(4, '0')}`,
  }),
);

type ApiState = 'ready' | 'loading' | 'error';

interface Scenario {
  readonly notes: readonly CreditNoteListItemDto[];
  readonly apiState: ApiState;
  readonly locale?: (typeof SUPPORTED_LANGUAGES)[number];
}

test.describe('credit-notes-list', () => {
  test('renders every status, dense values, responsive actions, and bounded data', async ({
    page,
  }) => {
    const scenario = {
      notes: [...STATUS_NOTES, ...DENSE_NOTES, LONG_NOTE],
      apiState: 'ready' as const,
    };
    const collector = collectBrowserFailures(page);
    try {
      await installCreditNotesFixture(page, scenario);
      await page.goto(ROUTE);
      await waitForStablePage(page);

      await expect(visibleEntries(page)).toHaveCount(20);
      for (const status of ['Active', 'Fully redeemed', 'Expired', 'Voided']) {
        await expect(
          page.locator('.credit-note-status').filter({ hasText: status }),
        ).not.toHaveCount(0);
      }
      await expect(page.locator('.credit-note-row').first()).toHaveAttribute('tabindex', '0');
      await assertResponsiveBounds(page, page.viewportSize()!.width);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('exercises search, status filters, clear, and detail actions', async ({ page }) => {
    const scenario = {
      notes: [...STATUS_NOTES, ...DENSE_NOTES, LONG_NOTE],
      apiState: 'ready' as const,
    };
    const collector = collectBrowserFailures(page);
    try {
      await installCreditNotesFixture(page, scenario);
      await page.goto(ROUTE);
      await waitForStablePage(page);

      const search = page.getByPlaceholder(/Search by code/i);
      const searched = waitForListRequest(
        page,
        (url) => url.searchParams.get('search') === 'LONG-DETERMINISTIC',
      );
      await search.fill('LONG-DETERMINISTIC');
      await searched;
      await expect(visibleEntries(page)).toHaveCount(1);
      await expect(visibleEntries(page)).toContainText(LONG_NOTE.customerName!);

      const resetSearch = waitForListRequest(page, (url) => !url.searchParams.has('search'));
      await search.fill('');
      await resetSearch;
      const filtered = waitForListRequest(
        page,
        (url) => url.searchParams.get('status') === 'Voided',
      );
      await page.locator('.credit-note-status-filter').click();
      await page.locator('.p-select-overlay').getByText('Voided', { exact: true }).click();
      await filtered;
      await expect(visibleEntries(page)).toHaveCount(1);

      const cleared = waitForListRequest(
        page,
        (url) => !url.searchParams.has('status') && !url.searchParams.has('search'),
      );
      await page.getByRole('button', { name: /Clear filters/i }).click();
      await cleared;
      await expect(visibleEntries(page)).toHaveCount(20);

      await visibleEntries(page).first().click();
      await expect(page.locator('.credit-note-detail')).toBeVisible();
      await expect(page.getByRole('button', { name: /Void credit note/i })).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('does not combine loading, empty, and error feedback', async ({ page }) => {
    for (const scenario of [
      { notes: [], apiState: 'loading' as const },
      { notes: [], apiState: 'ready' as const },
      { notes: [], apiState: 'error' as const },
    ]) {
      await installCreditNotesFixture(page, scenario);
      await page.goto(ROUTE);
      if (scenario.apiState === 'loading') {
        await expect(page.locator('.credit-notes-list[aria-busy="true"]')).toBeVisible();
      } else if (scenario.apiState === 'error') {
        await expect(page.getByRole('alert')).toBeVisible();
        await expect(page.locator('.credit-note-empty')).toHaveCount(0);
      } else {
        await expect(page.locator('.credit-note-empty:visible')).toBeVisible();
      }
      await page.unrouteAll({ behavior: 'ignoreErrors' });
    }
  });

  test('keeps cards, table scrolling, and localized values inside their profiles', async ({}, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'scoped browser and locale matrix');
    test.setTimeout(180_000);
    for (const browserType of [chromium, firefox, webkit]) {
      for (const viewport of VIEWPORTS) {
        await withCreditNotesPage(
          browserType,
          viewport,
          { notes: [LONG_NOTE], apiState: 'ready' },
          async (page) => {
            await assertResponsiveBounds(page, viewport.width);
          },
        );
      }
    }
    for (const locale of SUPPORTED_LANGUAGES) {
      await withCreditNotesPage(
        chromium,
        VIEWPORTS[0],
        { notes: [LONG_NOTE], apiState: 'ready', locale },
        async (page) => {
          await expect(
            page.locator('.credit-note-card:visible, .credit-note-table:visible').first(),
          ).toBeVisible();
          const translations = await page.evaluate(async (activeLocale) => {
            const response = await fetch(`/assets/i18n/${activeLocale}.json`);
            const values = (await response.json()) as {
              sales: { creditNotes: { status: { Active: string } } };
            };
            return values.sales.creditNotes.status.Active;
          }, locale);
          await expect(page.locator('.credit-note-status:visible')).toContainText(translations);
          await assertResponsiveBounds(page, VIEWPORTS[0].width);
        },
      );
    }
  });
});

async function installCreditNotesFixture(page: Page, scenario: Scenario): Promise<void> {
  await installShellFixture(page, createShellScenario({ locale: scenario.locale }));
  const state = [...scenario.notes];
  await page.route(`${API_BASE}/credit-notes**`, (route) =>
    handleCreditNoteRoute(route, state, scenario.apiState),
  );
}

async function handleCreditNoteRoute(
  route: Route,
  notes: CreditNoteListItemDto[],
  apiState: ApiState,
): Promise<void> {
  if (apiState === 'loading') return new Promise(() => undefined);
  if (apiState === 'error') return fulfillJson(route, { title: 'CreditNote.LoadFailed' }, 503);
  const url = new URL(route.request().url());
  const code = url.pathname.split('/').filter(Boolean).at(-1);
  if (route.request().method() === 'GET' && code && code !== 'credit-notes') {
    const item = notes.find((candidate) => candidate.code === decodeURIComponent(code));
    return fulfillJson(
      route,
      item ? toDetail(item) : { title: 'CreditNote.NotFound' },
      item ? 200 : 404,
    );
  }
  const search = (url.searchParams.get('search') ?? '').toLocaleLowerCase();
  const status = url.searchParams.get('status') ?? '';
  const page = Number(url.searchParams.get('page') ?? '1');
  const pageSize = Number(url.searchParams.get('pageSize') ?? '20');
  const matching = notes.filter(
    (item) =>
      (!status || item.status === status) && (!search || searchableText(item).includes(search)),
  );
  const start = (page - 1) * pageSize;
  return fulfillJson(route, {
    items: matching.slice(start, start + pageSize),
    totalCount: matching.length,
    pageNumber: page,
    pageSize,
  });
}

function searchableText(note: CreditNoteListItemDto): string {
  return [note.code, note.invoiceNumber, note.returnNumber, note.customerName]
    .filter(Boolean)
    .join(' ')
    .toLocaleLowerCase();
}

function toDetail(note: CreditNoteListItemDto) {
  return {
    ...note,
    isVoided: note.status === 'Voided',
    reason: 'Audit return reason',
    voidReason: null,
  };
}

function note(
  id: string,
  status: CreditNoteStatus,
  overrides: Partial<CreditNoteListItemDto> = {},
): CreditNoteListItemDto {
  return {
    creditNoteId: `credit-note-${id}`,
    code: `CN-${id.toUpperCase()}`,
    status,
    originalAmount: 500,
    availableBalance: 300,
    expiresAt: '2027-05-30T00:00:00.000Z',
    issuedAt: '2026-05-30T10:00:00.000Z',
    saleReturnId: `return-${id}`,
    returnNumber: `RET-${id.toUpperCase()}`,
    saleId: `sale-${id}`,
    invoiceNumber: `INV-${id.toUpperCase()}`,
    customerName: id === 'voided' ? null : 'Audit Customer',
    ...overrides,
  };
}

function fulfillJson(route: Route, body: unknown, status = 200): Promise<void> {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}

function waitForListRequest(page: Page, matches: (url: URL) => boolean): Promise<unknown> {
  return page.waitForRequest((request) => {
    if (request.method() !== 'GET') return false;
    const url = new URL(request.url());
    return url.pathname === '/api/credit-notes' && matches(url);
  });
}

async function withCreditNotesPage(
  browserType: BrowserType,
  viewport: (typeof VIEWPORTS)[number],
  scenario: Scenario,
  assertion: (page: Page) => Promise<void>,
): Promise<void> {
  const browser = await browserType.launch();
  const context = await browser.newContext({
    viewport,
    locale: scenario.locale,
    timezoneId: 'Asia/Kolkata',
    serviceWorkers: 'block',
  });
  const page = await context.newPage();
  const collector = collectBrowserFailures(page);
  try {
    await installCreditNotesFixture(page, scenario);
    await page.goto(ROUTE);
    await waitForStablePage(page);
    await assertion(page);
    assertNoUnexpectedBrowserFailures(collector.failures);
  } finally {
    collector.dispose();
    await context.close();
    await browser.close();
  }
}

async function assertResponsiveBounds(page: Page, width: number): Promise<void> {
  const metrics = await page.evaluate(() => {
    const table = document.querySelector<HTMLElement>('.credit-note-table');
    const surface = document.querySelector<HTMLElement>('.ledger-panel');
    const cards = Array.from(document.querySelectorAll<HTMLElement>('.credit-note-card'));
    const longValues = Array.from(
      document.querySelectorAll<HTMLElement>('[data-audit-long-value]'),
    );
    return {
      documentWidth: document.documentElement.scrollWidth,
      tableScrolls: !!table && table.scrollWidth > table.clientWidth,
      cardsInsideViewport: cards.every(
        (card) => card.getBoundingClientRect().right <= window.innerWidth,
      ),
      valuesTruncate: longValues.every((value) => value.scrollWidth >= value.clientWidth),
      surfaceVisible: !!surface && getComputedStyle(surface).display !== 'none',
    };
  });
  expect(metrics.documentWidth).toBeLessThanOrEqual(width);
  expect(metrics.surfaceVisible).toBe(true);
  expect(metrics.valuesTruncate).toBe(true);
  if (width <= 720) expect(metrics.cardsInsideViewport).toBe(true);
  if (width > 720 && width < 1100) expect(metrics.tableScrolls).toBe(true);
}

function visibleEntries(page: Page) {
  return page.locator('.credit-note-row:visible, .credit-note-card:visible');
}
