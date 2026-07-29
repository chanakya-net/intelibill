import { expect, test, type Locator, type Page, type Route } from '@playwright/test';
import { inflateSync } from 'node:zlib';

import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  waitForStablePage,
} from '../support/audit-page';
import {
  DENSE_SALE,
  LARGE_VALUE_SALE,
  LONG_ADDRESS_SALE,
  LONG_DESCRIPTION_TAIL_MARKER,
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
        await assertA4Output(page, 1, {
          requiredFragments: [
            sale.invoiceNumber,
            sale.customerName!,
            'Premium Widget',
            'INV-2026-001',
          ],
        });
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
        await assertA4Output(page, 2, {
          requiredFragments: [
            sale.invoiceNumber,
            sale.customerName,
            'Deterministic long invoice description for A4 pagination coverage.',
            LONG_DESCRIPTION_TAIL_MARKER,
          ],
          pageSpanFragments: {
            earlier: 'Deterministic long invoice description for A4 pagination coverage.',
            later: LONG_DESCRIPTION_TAIL_MARKER,
          },
        });
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
        await assertA4Output(page, 1, {
          requiredFragments: [
            DENSE_SALE.invoiceNumber,
            DENSE_SALE.customerName,
            'Item A',
            'Item B',
            'Installation Service',
            // Proves totals render intact alongside all dense items on the same
            // printed page, not merely in the pre-pagination DOM snapshot.
            formatCurrency(DENSE_SALE.totalAmount),
            'Grand Total',
          ],
        });
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
        await assertA4Output(page, 1, {
          requiredFragments: [LARGE_VALUE_SALE.invoiceNumber, 'Bulk Equipment'],
        });
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
        await assertA4Output(page, 1, {
          requiredFragments: [OPTIONAL_FIELDS_SALE.invoiceNumber, 'Basic Item', 'Walk-in'],
        });
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
        await assertThermalOutput(page, {
          requiredFragments: [
            sale.invoiceNumber,
            sale.customerName!,
            'Premium Widget',
            'Grand Total',
          ],
          maxHeightMm: 350,
          expectContentFittedHeight: true,
        });
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
        await assertThermalOutput(page, {
          requiredFragments: [
            LONG_ADDRESS_SALE.invoiceNumber,
            LONG_ADDRESS_SALE.customerName,
            'Deterministic long invoice description for A4 pagination coverage.',
          ],
          maxHeightMm: 350,
        });
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
        await assertThermalOutput(page, {
          requiredFragments: [DENSE_SALE.invoiceNumber, 'Item A', 'Item B', 'Installation Service'],
          maxHeightMm: 350,
          expectContentFittedHeight: true,
        });
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
        await assertThermalOutput(page, {
          requiredFragments: [LARGE_VALUE_SALE.invoiceNumber, 'Bulk Equipment'],
          maxHeightMm: 350,
          expectContentFittedHeight: true,
        });
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
        await assertThermalOutput(page, {
          requiredFragments: [OPTIONAL_FIELDS_SALE.invoiceNumber, 'Walk-in'],
          maxHeightMm: 350,
          expectContentFittedHeight: true,
        });
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

interface PdfPageArtifact {
  readonly width: number;
  readonly height: number;
  readonly text: string;
}

type FontToUnicodeMap = ReadonlyMap<string, string>;

interface PdfObjectRecord {
  readonly objectId: number;
  readonly dictionary: string;
  readonly stream?: string;
}

interface A4OutputExpectation {
  readonly requiredFragments?: readonly string[];
  /**
   * Proves content genuinely spans pages instead of the aggregate-fragment
   * check merely finding `earlier` near the top of the joined text. Asserts
   * `earlier` is found on a strictly earlier printed page than `later`.
   */
  readonly pageSpanFragments?: { readonly earlier: string; readonly later: string };
}

interface ThermalOutputExpectation {
  readonly requiredFragments?: readonly string[];
  readonly maxHeightMm?: number;
  /**
   * When true, asserts the printed page is sized to the actual rendered
   * content height (a short, content-fitted receipt) rather than a fixed
   * full-length page with a blank tail. Only valid for content short enough
   * to fit on a single content-fitted page (see THERMAL_MAX_SINGLE_PAGE_HEIGHT_MM
   * in sale-invoice-thermal.component.ts) — long content instead paginates.
   */
  readonly expectContentFittedHeight?: boolean;
}

const THERMAL_CONTENT_HEIGHT_TOLERANCE_MM = 6;

async function assertA4Output(
  page: Page,
  minimumPages: number,
  expectation: A4OutputExpectation = {},
): Promise<void> {
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

  const pdf = await page.pdf({ preferCSSPageSize: true, printBackground: true });
  const printedPages = parsePdfPages(pdf.toString('latin1'));
  expect(printedPages.length).toBeGreaterThanOrEqual(minimumPages);
  for (const pageArtifact of printedPages) {
    expect(pageArtifact.text.trim()).not.toEqual('');
    const { width: pageWidth, height: pageHeight } = pageArtifact;
    expect(pageWidth).toBeGreaterThanOrEqual(A4_WIDTH_PT - PDF_DIMENSION_TOLERANCE_PT);
    expect(pageWidth).toBeLessThanOrEqual(A4_WIDTH_PT + PDF_DIMENSION_TOLERANCE_PT);
    expect(pageHeight).toBeGreaterThanOrEqual(A4_HEIGHT_PT - PDF_DIMENSION_TOLERANCE_PT);
    expect(pageHeight).toBeLessThanOrEqual(A4_HEIGHT_PT + PDF_DIMENSION_TOLERANCE_PT);
  }

  if ((expectation.requiredFragments?.length ?? 0) > 0) {
    assertPdfFragmentsExist(printedPages, expectation.requiredFragments ?? []);
  }

  if (expectation.pageSpanFragments) {
    assertPdfFragmentSpansPages(
      printedPages,
      expectation.pageSpanFragments.earlier,
      expectation.pageSpanFragments.later,
    );
  }

  const mediaBoxes = extractMediaBoxesPt(pdf.toString('latin1'));
  expect(mediaBoxes.length).toBeGreaterThan(0);
  for (const box of mediaBoxes) {
    expect(box.width).toBeGreaterThanOrEqual(A4_WIDTH_PT - PDF_DIMENSION_TOLERANCE_PT);
    expect(box.width).toBeLessThanOrEqual(A4_WIDTH_PT + PDF_DIMENSION_TOLERANCE_PT);
    expect(box.height).toBeGreaterThanOrEqual(A4_HEIGHT_PT - PDF_DIMENSION_TOLERANCE_PT);
    expect(box.height).toBeLessThanOrEqual(A4_HEIGHT_PT + PDF_DIMENSION_TOLERANCE_PT);
  }
}

async function assertThermalOutput(
  page: Page,
  expectation: ThermalOutputExpectation = {},
): Promise<void> {
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
      contentHeightPx: article.scrollHeight,
    };
  });

  const mmWidth = (output.width / 96) * 25.4;
  expect(mmWidth).toBeLessThanOrEqual(80);
  expect(output.font).toContain('Courier New');
  expect(output.overflow).toBe(false);
  expect(output.pageRule.toLowerCase()).toContain('80mm');
  const contentHeightMm = (output.contentHeightPx / 96) * 25.4;

  // Generate the actual thermal print artifact and prove the printer-selected
  // width comes from the 80mm @page CSS rule, not an assumption about the DOM.
  const pdf = await page.pdf({ preferCSSPageSize: true, printBackground: true });
  const printedPages = parsePdfPages(pdf.toString('latin1'));
  const mediaBoxes = extractMediaBoxesPt(pdf.toString('latin1'));
  expect(mediaBoxes.length).toBeGreaterThan(0);
  for (const box of mediaBoxes) {
    expect(box.width).toBeGreaterThanOrEqual(THERMAL_WIDTH_PT - PDF_DIMENSION_TOLERANCE_PT);
    expect(box.width).toBeLessThanOrEqual(THERMAL_WIDTH_PT + PDF_DIMENSION_TOLERANCE_PT);
    if (expectation.maxHeightMm) {
      expect(box.height).toBeLessThanOrEqual(expectation.maxHeightMm * PT_PER_MM);
    }
  }

  if (expectation.expectContentFittedHeight) {
    // Proves the printed page is sized to actual content, not a fixed
    // full-length page — a regression to a fixed 297mm (or larger) page would
    // leave a blank tail far outside this tolerance band.
    expect(printedPages.length).toBe(1);
    expect(mediaBoxes.length).toBe(1);
    const pageHeightMm = mediaBoxes[0].height / PT_PER_MM;
    expect(pageHeightMm).toBeGreaterThanOrEqual(
      contentHeightMm - THERMAL_CONTENT_HEIGHT_TOLERANCE_MM,
    );
    expect(pageHeightMm).toBeLessThanOrEqual(contentHeightMm + THERMAL_CONTENT_HEIGHT_TOLERANCE_MM);
  }

  expect(printedPages.length).toBeGreaterThanOrEqual(1);
  for (const pageArtifact of printedPages) {
    expect(pageArtifact.text.trim()).not.toEqual('');
    if (expectation.maxHeightMm) {
      expect(pageArtifact.height).toBeLessThanOrEqual(expectation.maxHeightMm * PT_PER_MM);
    }
  }

  if ((expectation.requiredFragments?.length ?? 0) > 0) {
    assertPdfFragmentsExist(printedPages, expectation.requiredFragments ?? []);
  }
}

function assertPdfFragmentsExist(
  printedPages: readonly PdfPageArtifact[],
  requiredFragments: readonly string[],
): void {
  const pdfText = normalizePdfText(printedPages.map((page) => page.text).join(' '));
  for (const rawFragment of requiredFragments) {
    const normalized = normalizePdfText(rawFragment);
    expect(pdfText).toContain(normalized);
  }
}

function assertPdfFragmentSpansPages(
  printedPages: readonly PdfPageArtifact[],
  earlierFragment: string,
  laterFragment: string,
): void {
  const normalizedEarlier = normalizePdfText(earlierFragment);
  const normalizedLater = normalizePdfText(laterFragment);
  const earlierPageIndex = printedPages.findIndex((pageArtifact) =>
    normalizePdfText(pageArtifact.text).includes(normalizedEarlier),
  );
  const laterPageIndex = printedPages.findIndex((pageArtifact) =>
    normalizePdfText(pageArtifact.text).includes(normalizedLater),
  );
  expect(
    earlierPageIndex,
    'earlier fragment must be present on a printed page',
  ).toBeGreaterThanOrEqual(0);
  expect(laterPageIndex, 'later fragment must be present on a printed page').toBeGreaterThanOrEqual(
    0,
  );
  expect(
    laterPageIndex,
    'later fragment must land on a strictly later page than the earlier fragment, proving content is not cropped mid-pagination',
  ).toBeGreaterThan(earlierPageIndex);
}

function parsePdfPages(pdfText: string): PdfPageArtifact[] {
  const objects = parsePdfObjects(pdfText);
  const objectsById = new Map(
    objects.map((object): [number, PdfObjectRecord] => [object.objectId, object]),
  );
  const toUnicodeCache = new Map<number, FontToUnicodeMap | null>();
  const pageRecords = objects
    .filter((object) => /\/Type\s*\/Page\b/.test(object.dictionary))
    .sort((a, b) => a.objectId - b.objectId);

  return pageRecords
    .map((pageRecord) => {
      const mediaBox = extractMediaBox(pageRecord.dictionary) ?? {
        width: 0,
        height: 0,
      };
      const contents = parseContents(pageRecord.dictionary)
        .map((objectId) => objectsById.get(objectId))
        .filter((object): object is PdfObjectRecord => Boolean(object));
      const fontMap = buildPageFontMaps(
        extractFontResourceIds(pageRecord.dictionary, objectsById),
        objectsById,
        toUnicodeCache,
      );

      const text = normalizePdfText(
        contents
          .map((content) =>
            content.stream
              ? normalizePdfText(extractPdfTextContent(content, content.dictionary, fontMap))
              : '',
          )
          .join(' '),
      );
      return { ...mediaBox, text };
    })
    .filter((page) => page.width > 0 && page.height > 0);
}

function parsePdfObjects(pdfText: string): PdfObjectRecord[] {
  const objectPattern = /(\d+)\s+\d+\s+obj([\s\S]*?)(?=endobj)/g;
  const objects: PdfObjectRecord[] = [];
  let objectMatch: RegExpExecArray | null;

  while ((objectMatch = objectPattern.exec(pdfText)) !== null) {
    const [, objectId, rawBody] = objectMatch;
    const streamMatch = /stream\r?\n([\s\S]*?)\r?\nendstream/.exec(rawBody);
    objects.push({
      objectId: Number(objectId),
      dictionary: rawBody,
      stream: streamMatch?.[1],
    });
  }
  return objects;
}

function parseContents(pageDictionary: string): number[] {
  const contents = /\/Contents\s+(\[[^\]]*\]|\d+\s+\d+\s+R)/.exec(pageDictionary);
  if (!contents) {
    return [];
  }

  const referenceBlock = contents[1];
  const referenceMatch = Array.from(referenceBlock.matchAll(/(\d+)\s+\d+\s+R/g));
  if (referenceMatch.length > 0) {
    return referenceMatch.map((match) => Number(match[1]));
  }

  const single = /(\d+)\s+\d+\s+R/.exec(referenceBlock);
  return single ? [Number(single[1])] : [];
}

function extractMediaBox(pageDictionary: string): { width: number; height: number } | null {
  const mediaMatch = /\/MediaBox\s*\[([^\]]+)\]/.exec(pageDictionary);
  const values =
    mediaMatch?.[1]
      ?.split(/\s+/)
      .map((value) => Number.parseFloat(value))
      ?.filter((value) => Number.isFinite(value)) ?? [];
  if (values.length < 4) {
    return null;
  }
  return { width: values[2] - values[0], height: values[3] - values[1] };
}

function extractFontResourceIds(
  pageDictionary: string,
  objectsById: Map<number, PdfObjectRecord>,
): Map<string, number> {
  let currentDictionary: string | undefined = pageDictionary;
  const visitedPages = new Set<number>();
  while (currentDictionary) {
    const inlineFontMatch = /\/Font\s*<<([\s\S]*?)>>/.exec(currentDictionary);
    if (inlineFontMatch) {
      return parseFontRefs(inlineFontMatch[1]);
    }

    const resourcesMatch = /\/Resources\s+(\d+)\s+\d+\s+R/.exec(currentDictionary);
    const resourcesObject = resourcesMatch ? objectsById.get(Number(resourcesMatch[1])) : undefined;
    if (resourcesObject) {
      const fontMatch = /\/Font\s*<<([\s\S]*?)>>/.exec(resourcesObject.dictionary);
      if (fontMatch) {
        return parseFontRefs(fontMatch[1]);
      }
      currentDictionary = resourcesObject.dictionary;
      continue;
    }

    const parentObjectId = /\/Parent\s+(\d+)\s+\d+\s+R/.exec(currentDictionary)?.[1];
    if (!parentObjectId) {
      return new Map();
    }

    const parentObject = objectsById.get(Number(parentObjectId));
    if (!parentObject || visitedPages.has(parentObject.objectId)) {
      return new Map();
    }

    visitedPages.add(parentObject.objectId);
    currentDictionary = parentObject.dictionary;
  }
  return new Map();
}

function parseFontRefs(fontSection: string): Map<string, number> {
  const entries = new Map<string, number>();
  const fontMatch = /\/([A-Za-z0-9#]+)\s+(\d+)\s+\d+\s+R/g;
  let fontMatchResult: RegExpExecArray | null;
  while ((fontMatchResult = fontMatch.exec(fontSection)) !== null) {
    const [, fontName, objectId] = fontMatchResult;
    entries.set(fontName, Number(objectId));
  }

  return entries;
}

function buildPageFontMaps(
  fontReferences: Map<string, number>,
  objectsById: Map<number, PdfObjectRecord>,
  toUnicodeCache: Map<number, FontToUnicodeMap | null>,
): Map<string, FontToUnicodeMap> {
  const fontMaps = new Map<string, FontToUnicodeMap>();
  for (const [fontName, fontObjectId] of fontReferences) {
    const unicodeMap = getFontToUnicodeMap(fontObjectId, objectsById, toUnicodeCache);
    if (unicodeMap) {
      fontMaps.set(fontName, unicodeMap);
    }
  }
  return fontMaps;
}

function getFontToUnicodeMap(
  fontObjectId: number,
  objectsById: Map<number, PdfObjectRecord>,
  toUnicodeCache: Map<number, FontToUnicodeMap | null>,
): FontToUnicodeMap | null {
  const cached = toUnicodeCache.get(fontObjectId);
  if (cached !== undefined) {
    return cached;
  }

  const fontObject = objectsById.get(fontObjectId);
  const toUnicodeMatch = /\/ToUnicode\s+(\d+)\s+\d+\s+R/.exec(fontObject?.dictionary ?? '');
  if (!toUnicodeMatch) {
    toUnicodeCache.set(fontObjectId, null);
    return null;
  }

  const toUnicodeObject = objectsById.get(Number(toUnicodeMatch[1]));
  if (!toUnicodeObject) {
    toUnicodeCache.set(fontObjectId, null);
    return null;
  }

  const map = parseToUnicodeMap(toUnicodeObject);
  toUnicodeCache.set(fontObjectId, map.size > 0 ? map : null);
  return map.size > 0 ? map : null;
}

function extractPdfTextContent(
  content: PdfObjectRecord,
  dictionary: string,
  fontMaps: Map<string, FontToUnicodeMap>,
): string {
  const isFlate = /\/Filter\s*\/?\[?[^\]]*\/FlateDecode/.test(dictionary);
  const raw = content.stream ? Buffer.from(content.stream, 'latin1') : Buffer.alloc(0);
  const decoded = isFlate ? inflateSync(raw) : raw;
  const contentText = decoded.toString('latin1');
  const tokenPattern =
    /\/([^\s]+)\s+\d+(?:\.\d+)?\s+Tf|<([0-9A-Fa-f\s]+)>\s+Tj|(\((?:\\.|[^\\)])*\))\s+Tj|\[(.*?)\]\s+TJ|<([0-9A-Fa-f\s]+)>\s+TJ/g;
  const tokens = contentText.matchAll(tokenPattern);
  const textParts: string[] = [];
  let activeFont: string | undefined;
  for (const tokenMatch of tokens) {
    const [, fontName, hexText, literalText, arrayContent, tjHexText] = tokenMatch;
    if (fontName) {
      activeFont = fontName;
      continue;
    }

    if (hexText) {
      textParts.push(decodePdfHexTextByFont(hexText, activeFont, fontMaps));
      continue;
    }

    if (literalText) {
      textParts.push(decodePdfLiteralText(literalText));
      continue;
    }

    const values = tjHexText ? [decodePdfHexTextByFont(tjHexText, activeFont, fontMaps)] : [];
    if (arrayContent) {
      const inlineHexes = Array.from(
        arrayContent.matchAll(/<([0-9A-Fa-f\s]+)>|\((?:\\.|[^\\)])*\)/g),
      );
      const decodedArrayText = inlineHexes
        .map((inlineMatch) =>
          inlineMatch[1]
            ? decodePdfHexTextByFont(inlineMatch[1], activeFont, fontMaps)
            : decodePdfLiteralText(inlineMatch[0]),
        )
        .filter((fragment) => fragment.length > 0)
        .join(' ');
      if (decodedArrayText.length > 0) {
        values.push(decodedArrayText);
      }
    }

    textParts.push(values.filter(Boolean).join(' '));
  }

  return textParts.filter(Boolean).join(' ');
}

function decodePdfHexTextByFont(
  value: string,
  activeFont: string | undefined,
  fontMaps: Map<string, FontToUnicodeMap>,
): string {
  const map = activeFont ? fontMaps.get(activeFont) : undefined;
  if (!map || map.size === 0) {
    return decodePdfHexText(value);
  }

  const normalized = value.replace(/\s+/g, '').toUpperCase();
  if (normalized.length === 0) {
    return '';
  }
  const keyLength = map.keys().next().value?.length ?? 4;
  const hexChunks = chunkHexString(normalized, keyLength);
  const decoded = hexChunks
    .map((chunk) => map.get(chunk) ?? map.get(chunk.padEnd(keyLength, '0')))
    .filter((value) => value !== undefined)
    .join('');
  if (decoded.length > 0) {
    return decoded;
  }

  const utf16Text = decodePdfHexUtf16(value);
  return utf16Text.length > 0 ? utf16Text : decodePdfHexText(value);
}

function chunkHexString(value: string, chunkLength: number): string[] {
  const chunks: string[] = [];
  for (let index = 0; index + chunkLength <= value.length; index += chunkLength) {
    chunks.push(value.slice(index, index + chunkLength));
  }
  return chunks;
}

function parseToUnicodeMap(object: PdfObjectRecord): FontToUnicodeMap {
  const isFlate = /\/Filter\s*\/?\[?[^\]]*\/FlateDecode/.test(object.dictionary);
  const raw = object.stream ? Buffer.from(object.stream, 'latin1') : Buffer.alloc(0);
  const decoded = isFlate ? inflateSync(raw) : raw;
  const cmapText = decoded.toString('latin1');
  const map = new Map<string, string>();
  parseToUnicodeBfchar(cmapText, map);
  parseToUnicodeBfrange(cmapText, map);
  return map;
}

function parseToUnicodeBfchar(cmapText: string, map: FontToUnicodeMap): void {
  const bfcharPattern = /beginbfchar\s*([\s\S]*?)endbfchar/g;
  let blockMatch: RegExpExecArray | null;

  while ((blockMatch = bfcharPattern.exec(cmapText)) !== null) {
    const block = blockMatch[1];
    const mappingPattern = /<([0-9A-Fa-f]+)>\s+<([0-9A-Fa-f]+)>/g;
    let mappingMatch: RegExpExecArray | null;
    while ((mappingMatch = mappingPattern.exec(block)) !== null) {
      const [, source, mapped] = mappingMatch;
      map.set(source.toUpperCase(), decodeUnicodeHexToText(mapped));
    }
  }
}

function parseToUnicodeBfrange(cmapText: string, map: FontToUnicodeMap): void {
  const bfrangePattern = /beginbfrange\s*([\s\S]*?)endbfrange/g;
  let blockMatch: RegExpExecArray | null;

  while ((blockMatch = bfrangePattern.exec(cmapText)) !== null) {
    const block = blockMatch[1];
    const lines = block.split(/\r?\n/);
    for (const line of lines) {
      const tokens = line.match(/<[^>]+>|\[[^\]]*\]/g);
      if (!tokens || tokens.length < 3) {
        continue;
      }

      const sourceStart = tokens[0]?.replace(/[<>]/g, '');
      const sourceEnd = tokens[1]?.replace(/[<>]/g, '');
      if (!sourceStart || !sourceEnd) {
        continue;
      }

      const destinationRange = tokens[2];
      const rangeStart = Number.parseInt(sourceStart, 16);
      const rangeEnd = Number.parseInt(sourceEnd, 16);
      if (!Number.isFinite(rangeStart) || !Number.isFinite(rangeEnd) || rangeStart > rangeEnd) {
        continue;
      }

      const destinationTokens = destinationRange.startsWith('[')
        ? Array.from(destinationRange.matchAll(/<([^>]+)>/g)).map((match) => match[1])
        : [destinationRange.replace(/[<>]/g, '')];
      if (destinationTokens.length === 0) {
        continue;
      }

      for (let sourceCode = rangeStart; sourceCode <= rangeEnd; sourceCode += 1) {
        const target = normalizeBfrangeDestination(destinationTokens, sourceCode, rangeStart);
        if (target.length > 0) {
          map.set(sourceCode.toString(16).toUpperCase().padStart(sourceStart.length, '0'), target);
        }
      }
    }
  }
}

function normalizeBfrangeDestination(
  destinations: readonly string[],
  sourceCode: number,
  rangeStart: number,
): string {
  if (destinations.length > 1) {
    const destination = destinations[sourceCode - rangeStart];
    return destination ? decodeUnicodeHexToText(destination) : '';
  }

  const destinationText = destinations[0] ? decodeUnicodeHexToText(destinations[0]) : '';
  if (destinationText.length <= 1) {
    const offsetCode = destinationText.codePointAt(0);
    return offsetCode === undefined
      ? ''
      : String.fromCodePoint(offsetCode + (sourceCode - rangeStart));
  }

  if (sourceCode === rangeStart) {
    return destinationText;
  }

  return '';
}

function decodeUnicodeHexToText(value: string): string {
  const cleaned = value.replace(/\s+/g, '');
  let decoded = '';
  for (let index = 0; index + 3 < cleaned.length; index += 4) {
    const code = Number.parseInt(cleaned.slice(index, index + 4), 16);
    if (!Number.isNaN(code)) {
      decoded += String.fromCodePoint(code);
    }
  }
  return decoded;
}

function decodePdfLiteralText(token: string): string {
  const raw = token.slice(1, -1);
  let value = '';

  for (let index = 0; index < raw.length; index += 1) {
    const char = raw[index];
    if (char !== '\\') {
      value += char;
      continue;
    }

    const escape = raw[index + 1];
    if (!escape) {
      break;
    }

    index += 1;
    if (/\d/.test(escape)) {
      const octal = `${escape}${raw[index + 1] ?? ''}${raw[index + 2] ?? ''}`.replace(
        /[^0-7].*/,
        '',
      );
      index += octal.length - 1;
      value += String.fromCharCode(Number.parseInt(octal, 8));
      continue;
    }

    value +=
      {
        n: '\n',
        r: '\r',
        t: '\t',
        b: '\b',
        f: '\f',
        '(': '(',
        ')': ')',
        '\\': '\\',
      }[escape] ?? escape;
  }
  return value;
}

function decodePdfHexText(value: string): string {
  const tokens = value.replace(/\s+/g, '');
  const pairs = tokens.length % 2 === 0 ? tokens : `${tokens}0`;
  let decoded = '';
  for (let index = 0; index < pairs.length; index += 2) {
    const byte = Number.parseInt(pairs.slice(index, index + 2), 16);
    decoded += Number.isNaN(byte) ? '' : String.fromCharCode(byte);
  }
  return decoded;
}

function decodePdfHexUtf16(value: string): string {
  const tokens = value.replace(/\s+/g, '');
  if (tokens.length % 4 !== 0) {
    return '';
  }

  let decoded = '';
  for (let index = 0; index + 3 < tokens.length; index += 4) {
    const codePoint = Number.parseInt(tokens.slice(index, index + 4), 16);
    if (!Number.isNaN(codePoint)) {
      decoded += String.fromCodePoint(codePoint);
    }
  }
  return decoded;
}

function normalizePdfText(text: string): string {
  return text
    .replace(/\u0000/g, '')
    .replace(/\s+/g, '')
    .trim();
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
