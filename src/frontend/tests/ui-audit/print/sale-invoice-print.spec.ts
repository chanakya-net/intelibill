import { expect, test, type Locator, type Page, type Route } from '@playwright/test';

import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  waitForStablePage,
} from '../support/audit-page';
import {
  DENSE_SALE,
  LARGE_VALUE_SALE,
  LONG_ADDRESS_SALE,
  NORMAL_SALE,
  OPTIONAL_FIELDS_SALE,
  createSaleInvoiceScenario,
  type SaleInvoiceScenario,
} from '../fixtures/sale-invoices.fixture';
import { installShellFixture } from '../fixtures/shell.fixture';
import type { SaleDto } from '../../../src/app/features/sales/services/sale.models';
import type { SaleInvoiceApiState } from '../fixtures/sale-invoices.fixture';

const ROUTE = '/sales';
test.describe('sale-invoice-print (A4 media audit)', () => {
  test('renders normal document sections, totals, deterministic fonts, and no controls', async ({
    page,
  }) => {
    const sale = NORMAL_SALE;
    await withPrintDocument(
      page,
      createSaleInvoiceScenario({ sales: [sale] }),
      sale.saleId,
      'a4',
      async () => {
        await assertDocumentSections(page);
        await assertScreenControlsHidden(page);
        await assertExactPrintFonts(page);
        await assertA4Totals(page, sale);
        await assertA4Output(page, 1);
      },
    );
  });

  test('keeps long address and content on at least two unclipped A4 pages', async ({ page }) => {
    const sale = LONG_ADDRESS_SALE;
    await withPrintDocument(
      page,
      createSaleInvoiceScenario({ sales: [sale], longAddress: true }),
      sale.saleId,
      'a4',
      async () => {
        await assertDocumentSections(page);
        await assertA4Output(page, 2);
        await assertNoClippingOrOverlap(page);
      },
    );
  });

  test('keeps dense document panels, totals, and items apart', async ({ page }) => {
    await withPrintDocument(
      page,
      createSaleInvoiceScenario({ sales: [DENSE_SALE] }),
      DENSE_SALE.saleId,
      'a4',
      async () => {
        await assertDocumentSections(page);
        await assertA4Totals(page, DENSE_SALE);
        await assertA4Output(page, 1);
        await assertNoClippingOrOverlap(page);
      },
    );
  });

  test('renders large currency values without clipping totals', async ({ page }) => {
    await withPrintDocument(
      page,
      createSaleInvoiceScenario({ sales: [LARGE_VALUE_SALE] }),
      LARGE_VALUE_SALE.saleId,
      'a4',
      async () => {
        await assertA4Totals(page, LARGE_VALUE_SALE);
        await assertA4Output(page, 1);
        await assertNoClippingOrOverlap(page);
      },
    );
  });

  test('renders optional fields correctly when customer is walk-in', async ({ page }) => {
    await withPrintDocument(
      page,
      createSaleInvoiceScenario({
        sales: [OPTIONAL_FIELDS_SALE],
        optionalShopFields: true,
      }),
      OPTIONAL_FIELDS_SALE.saleId,
      'a4',
      async () => {
        await expect(page.locator('.invoice__customer')).toContainText('Walk-in');
        await expect(page.locator('.invoice__customer-phone')).toHaveCount(0);
        await expect(page.locator('.invoice__shop-phone, .invoice__shop-gst')).toHaveCount(0);
        await assertDocumentSections(page);
        await assertA4Totals(page, OPTIONAL_FIELDS_SALE);
        await assertA4Output(page, 1);
      },
    );
  });

  test('renders the loading print document without document sections or controls', async ({
    page,
  }) => {
    const saleId = NORMAL_SALE.saleId;
    await withPrintState(
      page,
      createSaleInvoiceScenario({ apiState: 'loading' }),
      saleId,
      'a4',
      async () => {
        await expect(page.locator('.print-state')).toBeVisible();
        await expect(page.locator('article.invoice')).toHaveCount(0);
        await assertScreenControlsHidden(page);
      },
    );
  });

  test('renders the declared error print document without document sections or controls', async ({
    page,
  }) => {
    const saleId = NORMAL_SALE.saleId;
    const declaredError = {
      url: `http://localhost:5277/api/sales/${saleId}`,
      status: 503,
    };
    await withPrintState(
      page,
      createSaleInvoiceScenario({ apiState: 'error' }),
      saleId,
      'a4',
      async () => {
        await expect(page.locator('.print-state--error[role="alert"]')).toBeVisible();
        await expect(page.locator('article.invoice')).toHaveCount(0);
        await assertScreenControlsHidden(page);
      },
      declaredError,
    );
  });
});

test.describe('sale-invoice-print (80mm thermal media audit)', () => {
  test('renders normal thermal document without clipping to receipt width', async ({ page }) => {
    const sale = NORMAL_SALE;
    await withPrintDocument(
      page,
      createSaleInvoiceScenario({ sales: [sale] }),
      sale.saleId,
      'thermal',
      async () => {
        await assertThermalDocumentSections(page);
        await assertScreenControlsHidden(page);
        await assertThermalTotals(page, sale);
        await assertThermalOutput(page);
      },
    );
  });

  test('keeps long address and content within thermal receipt width', async ({ page }) => {
    await withPrintDocument(
      page,
      createSaleInvoiceScenario({ sales: [LONG_ADDRESS_SALE], longAddress: true }),
      LONG_ADDRESS_SALE.saleId,
      'thermal',
      async () => {
        await assertThermalDocumentSections(page);
        await assertThermalOutput(page);
        await assertNoClippingOrOverlapThermal(page);
      },
    );
  });

  test('renders dense thermal document without overflow', async ({ page }) => {
    await withPrintDocument(
      page,
      createSaleInvoiceScenario({ sales: [DENSE_SALE] }),
      DENSE_SALE.saleId,
      'thermal',
      async () => {
        await assertThermalDocumentSections(page);
        await assertThermalTotals(page, DENSE_SALE);
        await assertThermalOutput(page);
        await assertNoClippingOrOverlapThermal(page);
      },
    );
  });

  test('renders large values without horizontal overflow on 80mm width', async ({ page }) => {
    await withPrintDocument(
      page,
      createSaleInvoiceScenario({ sales: [LARGE_VALUE_SALE] }),
      LARGE_VALUE_SALE.saleId,
      'thermal',
      async () => {
        await assertThermalDocumentSections(page);
        await assertThermalTotals(page, LARGE_VALUE_SALE);
        await assertThermalOutput(page);
        await assertNoClippingOrOverlapThermal(page);
      },
    );
  });

  test('renders optional customer and shop fields without empty rows', async ({ page }) => {
    await withPrintDocument(
      page,
      createSaleInvoiceScenario({
        sales: [OPTIONAL_FIELDS_SALE],
        optionalShopFields: true,
      }),
      OPTIONAL_FIELDS_SALE.saleId,
      'thermal',
      async () => {
        await expect(page.locator('.thermal-invoice__customer')).toContainText('Walk-in');
        await expect(
          page.locator('.thermal-invoice__shop-phone, .thermal-invoice__shop-gst'),
        ).toHaveCount(0);
        await assertThermalTotals(page, OPTIONAL_FIELDS_SALE);
        await assertThermalOutput(page);
      },
    );
  });

  test('renders loading and error thermal documents without invoice content', async ({ page }) => {
    await withPrintState(
      page,
      createSaleInvoiceScenario({ apiState: 'loading' }),
      NORMAL_SALE.saleId,
      'thermal',
      async () => {
        await expect(page.locator('.print-state')).toBeVisible();
        await expect(page.locator('article.invoice')).toHaveCount(0);
        await assertScreenControlsHidden(page);
      },
    );

    const declaredError = {
      url: `http://localhost:5277/api/sales/${NORMAL_SALE.saleId}`,
      status: 503,
    };
    await withPrintState(
      page,
      createSaleInvoiceScenario({ apiState: 'error' }),
      NORMAL_SALE.saleId,
      'thermal',
      async () => {
        await expect(page.locator('.print-state--error[role="alert"]')).toBeVisible();
        await expect(page.locator('article.invoice')).toHaveCount(0);
        await assertScreenControlsHidden(page);
      },
      declaredError,
    );
  });
});

async function withPrintDocument(
  page: Page,
  scenario: SaleInvoiceScenario,
  saleId: string,
  template: 'a4' | 'thermal',
  assertion: () => Promise<void>,
  declaredError?: DeclaredError,
): Promise<void> {
  await withPrintState(page, scenario, saleId, template, assertion, declaredError);
}

async function withPrintState(
  page: Page,
  scenario: SaleInvoiceScenario,
  saleId: string,
  template: 'a4' | 'thermal',
  assertion: () => Promise<void>,
  declaredError?: DeclaredError,
): Promise<void> {
  const collector = collectBrowserFailures(page);
  try {
    const saleState: SaleInvoiceFixtureState = {
      apiState: scenario.apiState,
      sales: new Map(scenario.sales.map((sale) => [sale.saleId, sale])),
    };

    // Install shell first to setup auth and base routes
    await installShellFixture(page, scenario.shell);

    // Then override sales routes for print-specific API calls
    await page.route('http://localhost:5277/api/sales/**', (route) =>
      handleSaleRoute(route, saleState),
    );

    const templateParam = template === 'thermal' ? '?template=thermal' : '';
    await page.goto(`${ROUTE}/${saleId}/print${templateParam}`);
    await page.emulateMedia({ media: 'print', colorScheme: 'light' });
    await waitForStablePage(page);
    await assertion();
    assertOnlyDeclaredFailures(collector.failures, declaredError);
  } finally {
    collector.dispose();
  }
}

interface SaleInvoiceFixtureState {
  readonly apiState: SaleInvoiceApiState;
  readonly sales: Map<string, SaleDto>;
}

function handleSaleRoute(route: Route, state: SaleInvoiceFixtureState): Promise<void> {
  if (state.apiState === 'loading') {
    return new Promise(() => undefined);
  }
  if (state.apiState === 'error') {
    return fulfillJson(route, { title: 'Sale.LoadFailed' }, 503);
  }

  const url = new URL(route.request().url());
  const segments = url.pathname.split('/').filter(Boolean);
  const pathIndex = segments.indexOf('sales');
  if (pathIndex === -1 || pathIndex + 1 >= segments.length) {
    return fulfillJson(route, { title: 'Sale.IdRequired' }, 400);
  }

  const saleId = decodeURIComponent(segments[pathIndex + 1]);
  const sale = state.sales.get(saleId);
  return fulfillJson(route, sale ?? { title: 'Sale.NotFound' }, sale ? 200 : 404);
}

async function fulfillJson(route: Route, body: unknown, status = 200): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  });
}

async function assertDocumentSections(page: Page): Promise<void> {
  await expect(page.locator('article.invoice')).toBeVisible();
  await expect(page.locator('.invoice__header')).toBeVisible();
  await expect(page.locator('.invoice__customer')).toBeVisible();
  await expect(page.locator('.invoice__items')).toBeVisible();
  await expect(page.locator('.invoice__totals')).toBeVisible();
}

async function assertThermalDocumentSections(page: Page): Promise<void> {
  await expect(page.locator('article.thermal-invoice')).toBeVisible();
  await expect(page.locator('.thermal-invoice__header')).toBeVisible();
  await expect(page.locator('.thermal-invoice__item').first()).toBeVisible();
}

async function assertScreenControlsHidden(page: Page): Promise<void> {
  await expect(page.locator('.screen-controls')).toBeHidden();
}

async function assertExactPrintFonts(page: Page): Promise<void> {
  const fonts = await page.locator('article.invoice').evaluate((article) => ({
    document: getComputedStyle(article).fontFamily,
    heading: getComputedStyle(article.querySelector('h1')!).fontFamily,
  }));
  expect(fonts.document).toBe('Arial, sans-serif');
  expect(fonts.heading).toBe('Georgia, "Times New Roman", serif');
}

const PT_PER_MM = 72 / 25.4;
const A4_WIDTH_PT = 210 * PT_PER_MM;
const A4_HEIGHT_PT = 297 * PT_PER_MM;
const THERMAL_WIDTH_PT = 80 * PT_PER_MM;
const PDF_DIMENSION_TOLERANCE_PT = 2;

async function assertA4Output(page: Page, minimumPages: number): Promise<void> {
  const printStyles = await page.locator('article.invoice').evaluate((article) => {
    const targets = [
      article.querySelector('.invoice__header'),
      article.querySelector('.invoice__customer'),
      article.querySelector('.invoice__items'),
      article.querySelector('.invoice__totals'),
    ];
    const pageRule = Array.from(document.styleSheets)
      .flatMap((sheet) => {
        try {
          return Array.from(sheet.cssRules);
        } catch {
          return [];
        }
      })
      .find((rule) => rule.cssText.includes('@page'));
    return {
      breaks: targets.map((target) => getComputedStyle(target!).breakInside),
      pageRule: pageRule?.cssText ?? '',
    };
  });
  expect(printStyles.breaks).toEqual(['avoid', 'avoid', 'avoid', 'avoid']);
  expect(printStyles.pageRule.toLowerCase()).toContain('size: a4');

  // Omit format/width/height so the generated MediaBox reflects the page's own
  // @page CSS rule (via preferCSSPageSize) rather than a size forced by the test.
  const pdf = await page.pdf({ preferCSSPageSize: true, printBackground: true });
  const pdfText = pdf.toString('latin1');
  const pageCount = (pdfText.match(/\/Type\s*\/Page\b/g) ?? []).length;
  expect(pageCount).toBeGreaterThanOrEqual(minimumPages);

  const mediaBoxes = extractMediaBoxesPt(pdfText);
  expect(mediaBoxes.length).toBeGreaterThan(0);
  for (const box of mediaBoxes) {
    expect(box.width).toBeGreaterThanOrEqual(A4_WIDTH_PT - PDF_DIMENSION_TOLERANCE_PT);
    expect(box.width).toBeLessThanOrEqual(A4_WIDTH_PT + PDF_DIMENSION_TOLERANCE_PT);
    expect(box.height).toBeGreaterThanOrEqual(A4_HEIGHT_PT - PDF_DIMENSION_TOLERANCE_PT);
    expect(box.height).toBeLessThanOrEqual(A4_HEIGHT_PT + PDF_DIMENSION_TOLERANCE_PT);
  }
}

async function assertThermalOutput(page: Page): Promise<void> {
  const thermalArticle = page.locator('article.thermal-invoice');
  await expect(thermalArticle).toBeVisible();

  const output = await thermalArticle.evaluate((article) => {
    const style = getComputedStyle(article);
    const pageRule = Array.from(document.styleSheets)
      .flatMap((sheet) => {
        try {
          return Array.from(sheet.cssRules);
        } catch {
          return [];
        }
      })
      .find((rule) => rule.cssText.includes('@page'));
    return {
      width: parseFloat(style.width),
      font: style.fontFamily,
      overflow: article.scrollWidth > article.clientWidth,
      pageRule: pageRule?.cssText ?? '',
    };
  });

  const mmWidth = (output.width / 96) * 25.4;
  expect(mmWidth).toBeLessThanOrEqual(80);
  expect(output.font).toContain('Courier New');
  expect(output.overflow).toBe(false);
  expect(output.pageRule.toLowerCase()).toContain('80mm');

  // Generate the actual thermal print artifact and prove the printer-selected
  // width comes from the 80mm @page CSS rule, not an assumption about the DOM.
  const pdf = await page.pdf({ preferCSSPageSize: true, printBackground: true });
  const mediaBoxes = extractMediaBoxesPt(pdf.toString('latin1'));
  expect(mediaBoxes.length).toBeGreaterThan(0);
  for (const box of mediaBoxes) {
    expect(box.width).toBeGreaterThanOrEqual(THERMAL_WIDTH_PT - PDF_DIMENSION_TOLERANCE_PT);
    expect(box.width).toBeLessThanOrEqual(THERMAL_WIDTH_PT + PDF_DIMENSION_TOLERANCE_PT);
  }
}

interface PdfPageBox {
  readonly width: number;
  readonly height: number;
}

function extractMediaBoxesPt(pdfText: string): PdfPageBox[] {
  const pattern = /\/MediaBox\s*\[\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*\]/g;
  const boxes: PdfPageBox[] = [];
  let match: RegExpExecArray | null;
  while ((match = pattern.exec(pdfText)) !== null) {
    const [, x0, y0, x1, y1] = match;
    boxes.push({ width: Number(x1) - Number(x0), height: Number(y1) - Number(y0) });
  }
  return boxes;
}

function formatCurrency(value: number): string {
  return `₹${value.toLocaleString('en-US', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`;
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function exactText(value: string): RegExp {
  return new RegExp(`^\\s*${escapeRegExp(value)}\\s*$`);
}

async function assertA4Totals(page: Page, sale: SaleDto): Promise<void> {
  await assertTotalsRow(
    page.locator('.invoice__totals-row'),
    '.invoice__totals-label',
    '.invoice__totals-value',
    'Subtotal (Before Discount)',
    formatCurrency(sale.totalBeforeDiscount),
  );
  await assertTotalsRow(
    page.locator('.invoice__totals-row'),
    '.invoice__totals-label',
    '.invoice__totals-value',
    'Discount',
    `-${formatCurrency(sale.totalDiscountAmount)}`,
  );
  await assertTotalsRow(
    page.locator('.invoice__totals-row'),
    '.invoice__totals-label',
    '.invoice__totals-value',
    'Tax',
    formatCurrency(sale.totalTaxAmount),
  );
  await assertTotalsRow(
    page.locator('.invoice__totals-row'),
    '.invoice__totals-label',
    '.invoice__totals-value',
    'Grand Total',
    formatCurrency(sale.totalAmount),
  );
}

async function assertThermalTotals(page: Page, sale: SaleDto): Promise<void> {
  await assertTotalsRow(
    page.locator('.thermal-invoice__summary-row'),
    'span >> nth=0',
    'span >> nth=1',
    'Subtotal (Before Discount)',
    formatCurrency(sale.totalBeforeDiscount),
  );
  await assertTotalsRow(
    page.locator('.thermal-invoice__summary-row'),
    'span >> nth=0',
    'span >> nth=1',
    'Discount',
    `-${formatCurrency(sale.totalDiscountAmount)}`,
  );
  await assertTotalsRow(
    page.locator('.thermal-invoice__summary-row'),
    'span >> nth=0',
    'span >> nth=1',
    'Tax',
    formatCurrency(sale.totalTaxAmount),
  );
  await assertTotalsRow(
    page.locator('.thermal-invoice__summary-row'),
    'span >> nth=0',
    'span >> nth=1',
    'Grand Total',
    formatCurrency(sale.totalAmount),
  );
}

async function assertTotalsRow(
  rows: Locator,
  labelSelector: string,
  valueSelector: string,
  label: string,
  expectedValue: string,
): Promise<void> {
  const row = rows.filter({
    has: rows.page().locator(labelSelector, { hasText: exactText(label) }),
  });
  await expect(row.locator(valueSelector)).toHaveText(expectedValue);
}

async function assertNoClippingOrOverlap(page: Page): Promise<void> {
  const result = await page.locator('article.invoice').evaluate((article) => {
    const elements = Array.from(
      article.querySelectorAll<HTMLElement>(
        '.invoice__header, .invoice__customer, .invoice__items, .invoice__totals',
      ),
    );
    const boxes = elements.map((element) => ({
      name: element.className,
      rect: element.getBoundingClientRect(),
      clipped:
        element.scrollHeight > element.clientHeight || element.scrollWidth > element.clientWidth,
    }));
    const overlap = boxes.some((box, index) =>
      boxes
        .slice(index + 1)
        .some(
          (other) =>
            box.rect.bottom > other.rect.top &&
            box.rect.top < other.rect.bottom &&
            box.rect.right > other.rect.left &&
            box.rect.left < other.rect.right,
        ),
    );
    return { clipped: boxes.filter((box) => box.clipped).map((box) => box.name), overlap };
  });
  expect(result.clipped).toEqual([]);
  expect(result.overlap).toBe(false);
}

async function assertNoClippingOrOverlapThermal(page: Page): Promise<void> {
  const result = await page.locator('article.thermal-invoice').evaluate((article) => {
    const elements = Array.from(
      article.querySelectorAll<HTMLElement>(
        '.thermal-invoice__header, .thermal-invoice__item, .thermal-invoice__totals',
      ),
    );
    const boxes = elements.map((element) => ({
      name: element.className,
      rect: element.getBoundingClientRect(),
      clipped:
        element.scrollHeight > element.clientHeight || element.scrollWidth > element.clientWidth,
    }));
    const horizontalOverflow = boxes
      .filter((box) => box.rect.width > article.clientWidth + 0.5)
      .map((box) => ({ name: box.name, width: box.rect.width }));
    return {
      clipped: boxes.filter((box) => box.clipped).map((box) => box.name),
      horizontalOverflow,
    };
  });
  expect(result.clipped).toEqual([]);
  expect(result.horizontalOverflow).toEqual([]);
}

interface DeclaredError {
  readonly url: string;
  readonly status: number;
}

function assertOnlyDeclaredFailures(
  failures: readonly { kind: string; message: string; url?: string }[],
  declaredError?: DeclaredError,
): void {
  if (!declaredError) {
    assertNoUnexpectedBrowserFailures(failures);
    return;
  }
  expect(failures).toContainEqual({
    kind: 'response',
    message: `HTTP ${declaredError.status}`,
    url: declaredError.url,
  });
  const unexpected = failures.filter((failure) => !isDeclaredFailure(failure, declaredError));
  assertNoUnexpectedBrowserFailures(unexpected);
}

function isDeclaredFailure(
  failure: { kind: string; message: string; url?: string },
  declaredError: DeclaredError,
): boolean {
  if (failure.kind === 'response')
    return failure.url === declaredError.url && failure.message === `HTTP ${declaredError.status}`;
  if (failure.kind === 'console')
    return failure.message.includes(`status of ${declaredError.status}`);
  return (
    failure.kind === 'request' &&
    failure.message === 'net::ERR_ABORTED' &&
    failure.url?.includes('/api/shops/')
  );
}
