import { expect, test, type Page } from '@playwright/test';

import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  waitForStablePage,
} from '../support/audit-page';
import {
  CREDIT_NOTE_STATUSES,
  DENSE_CREDIT_NOTE,
  LARGE_VALUE_CREDIT_NOTE,
  createCreditNoteScenario,
  installCreditNoteFixture,
  type CreditNoteScenario,
} from '../fixtures/credit-notes.fixture';

const ROUTE = '/sales/credit-notes';
const LONG_REASON = Array.from(
  { length: 350 },
  () => 'Deterministic long return explanation for A4 pagination coverage.',
).join(' ');

test.describe('credit-note-print (A4 media audit)', () => {
  test('renders normal document sections, totals, deterministic fonts, and no controls', async ({
    page,
  }) => {
    const creditNote = CREDIT_NOTE_STATUSES[0]!;
    await withPrintDocument(
      page,
      createCreditNoteScenario({ creditNotes: [creditNote] }),
      creditNote.code,
      async () => {
        await assertDocumentSections(page);
        await assertScreenControlsHidden(page);
        await assertExactPrintFonts(page);
        await assertA4Output(page, 1);
      },
    );
  });

  test('keeps long document content on at least two unclipped A4 pages', async ({ page }) => {
    const creditNote = { ...DENSE_CREDIT_NOTE, reason: LONG_REASON };
    await withPrintDocument(
      page,
      createCreditNoteScenario({ creditNotes: [creditNote] }),
      creditNote.code,
      async () => {
        await assertDocumentSections(page);
        await assertA4Output(page, 2);
        await assertNoClippingOrOverlap(page);
      },
    );
  });

  test('keeps dense document panels, totals, markers, and terms apart', async ({ page }) => {
    await withPrintDocument(
      page,
      createCreditNoteScenario({ creditNotes: [DENSE_CREDIT_NOTE] }),
      DENSE_CREDIT_NOTE.code,
      async () => {
        await assertDocumentSections(page);
        await assertA4Output(page, 1);
        await assertNoClippingOrOverlap(page);
      },
    );
  });

  test('renders large currency values without clipping totals', async ({ page }) => {
    await withPrintDocument(
      page,
      createCreditNoteScenario({ creditNotes: [LARGE_VALUE_CREDIT_NOTE] }),
      LARGE_VALUE_CREDIT_NOTE.code,
      async () => {
        await expect(page.locator('.credit-note__metric').first()).toContainText('99,999.99');
        await expect(page.locator('.credit-note__metric').nth(1)).toContainText('75,000.00');
        await assertA4Output(page, 1);
        await assertNoClippingOrOverlap(page);
      },
    );
  });

  test('renders the loading print document without document sections or controls', async ({
    page,
  }) => {
    const code = CREDIT_NOTE_STATUSES[0]!.code;
    await withPrintState(
      page,
      createCreditNoteScenario({ apiState: 'loading' }),
      code,
      async () => {
        await expect(page.locator('.print-state')).toBeVisible();
        await expect(page.locator('article.credit-note')).toHaveCount(0);
        await assertScreenControlsHidden(page);
      },
    );
  });

  test('renders the declared error print document without document sections or controls', async ({
    page,
  }) => {
    const code = CREDIT_NOTE_STATUSES[0]!.code;
    const declaredError = {
      url: `http://localhost:5277/api/credit-notes/${code}/print`,
      status: 503,
    };
    await withPrintState(
      page,
      createCreditNoteScenario({ apiState: 'error' }),
      code,
      async () => {
        await expect(page.locator('.print-state--error[role="alert"]')).toBeVisible();
        await expect(page.locator('article.credit-note')).toHaveCount(0);
        await assertScreenControlsHidden(page);
      },
      declaredError,
    );
  });
});

async function withPrintDocument(
  page: Page,
  scenario: CreditNoteScenario,
  code: string,
  assertion: () => Promise<void>,
  declaredError?: DeclaredError,
): Promise<void> {
  await withPrintState(page, scenario, code, assertion, declaredError);
}

async function withPrintState(
  page: Page,
  scenario: CreditNoteScenario,
  code: string,
  assertion: () => Promise<void>,
  declaredError?: DeclaredError,
): Promise<void> {
  const collector = collectBrowserFailures(page);
  try {
    await installCreditNoteFixture(page, scenario);
    await page.goto(`${ROUTE}/${code}/print`);
    await page.emulateMedia({ media: 'print', colorScheme: 'light' });
    await waitForStablePage(page);
    await assertion();
    assertOnlyDeclaredFailures(collector.failures, declaredError);
  } finally {
    collector.dispose();
  }
}

async function assertDocumentSections(page: Page): Promise<void> {
  await expect(page.locator('article.credit-note')).toBeVisible();
  await expect(page.locator('.credit-note__header')).toBeVisible();
  await expect(page.locator('.credit-note__panel')).toHaveCount(2);
  await expect(page.locator('.credit-note__totals')).toBeVisible();
  await expect(page.locator('.credit-note__terms')).toBeVisible();
}

async function assertScreenControlsHidden(page: Page): Promise<void> {
  await expect(page.locator('.screen-controls')).toBeHidden();
}

async function assertExactPrintFonts(page: Page): Promise<void> {
  const fonts = await page.locator('.credit-note').evaluate((article) => ({
    document: getComputedStyle(article).fontFamily,
    heading: getComputedStyle(article.querySelector('h1')!).fontFamily,
  }));
  expect(fonts.document).toBe('Arial, sans-serif');
  expect(fonts.heading).toBe('Georgia, "Times New Roman", serif');
}

async function assertA4Output(page: Page, minimumPages: number): Promise<void> {
  const printStyles = await page.locator('article.credit-note').evaluate((article) => {
    const targets = [
      article.querySelector('.credit-note__header'),
      article.querySelector('.credit-note__grid'),
      article.querySelector('.credit-note__totals'),
      article.querySelector('.credit-note__markers'),
      article.querySelector('.credit-note__terms'),
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
  expect(printStyles.breaks).toEqual(['avoid', 'avoid', 'avoid', 'avoid', 'avoid']);
  expect(printStyles.pageRule.toLowerCase()).toContain('size: a4');

  const pdf = await page.pdf({
    format: 'A4',
    margin: { top: '12mm', right: '12mm', bottom: '12mm', left: '12mm' },
    preferCSSPageSize: true,
    printBackground: true,
  });
  const pageCount = (pdf.toString('latin1').match(/\/Type\s*\/Page\b/g) ?? []).length;
  expect(pageCount).toBeGreaterThanOrEqual(minimumPages);
}

async function assertNoClippingOrOverlap(page: Page): Promise<void> {
  const result = await page.locator('article.credit-note').evaluate((article) => {
    const elements = Array.from(
      article.querySelectorAll<HTMLElement>(
        '.credit-note__header, .credit-note__panel, .credit-note__totals, .credit-note__markers, .credit-note__terms',
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
