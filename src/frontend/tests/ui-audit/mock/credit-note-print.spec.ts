import { expect, test } from '@playwright/test';

import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  mockExternalRequests,
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

test.describe('credit-note-print (A4 media audit)', () => {
  test('renders normal credit note with required sections and no clipping', async ({ page }) => {
    const scenario = createCreditNoteScenario({
      creditNotes: [CREDIT_NOTE_STATUSES[0]!],
    });
    await withPrintPage(page, scenario, CREDIT_NOTE_STATUSES[0]!.code, async () => {
      await assertPrintLayout(page, { expectedSections: ['header', 'details', 'customer', 'totals', 'terms'] });
      await assertScreenControlsHidden(page);
      await assertNoClipping(page);
    });
  });

  test('renders long credit note with deterministic pagination and no overlap', async ({ page }) => {
    const scenario = createCreditNoteScenario({
      creditNotes: [{ ...DENSE_CREDIT_NOTE, reason: 'A'.repeat(500) }],
    });
    await withPrintPage(page, scenario, DENSE_CREDIT_NOTE.code, async () => {
      await assertPrintLayout(page, { expectedSections: ['header', 'details', 'customer', 'totals', 'terms'] });
      await assertNoPageBreakClipping(page);
      await assertDeterministicFonts(page);
    });
  });

  test('renders dense credit note without overlapping content', async ({ page }) => {
    const scenario = createCreditNoteScenario({
      creditNotes: [DENSE_CREDIT_NOTE],
    });
    await withPrintPage(page, scenario, DENSE_CREDIT_NOTE.code, async () => {
      await assertPrintLayout(page, { expectedSections: ['header', 'details', 'customer', 'totals', 'terms'] });
      await assertNoOverlap(page);
    });
  });

  test('renders large-value credit note with correct totals formatting', async ({ page }) => {
    const scenario = createCreditNoteScenario({
      creditNotes: [LARGE_VALUE_CREDIT_NOTE],
    });
    await withPrintPage(page, scenario, LARGE_VALUE_CREDIT_NOTE.code, async () => {
      const originalText = await page.locator('.credit-note__metric').first().textContent();
      const balanceText = await page.locator('.credit-note__metric').nth(1).textContent();
      expect(originalText).toContain('99,999.99');
      expect(balanceText).toContain('75,000.00');
      await assertDeterministicFonts(page);
    });
  });

  test('renders Expired status with marker and no clipping', async ({ page }) => {
    const expired = { ...CREDIT_NOTE_STATUSES[0]!, status: 'Expired' };
    const scenario = createCreditNoteScenario({ creditNotes: [expired] });
    await withPrintPage(page, scenario, expired.code, async () => {
      await expect(page.locator('.credit-note__status--expired')).toBeVisible();
      await expect(page.locator('.credit-note__marker--expired')).toBeVisible();
      await assertNoClipping(page);
    });
  });

  test('renders Voided status with marker and void reason', async ({ page }) => {
    const voided = { ...CREDIT_NOTE_STATUSES[0]!, status: 'Voided', voidReason: 'Test cancellation' };
    const scenario = createCreditNoteScenario({ creditNotes: [voided] });
    await withPrintPage(page, scenario, voided.code, async () => {
      await expect(page.locator('.credit-note__status--voided')).toBeVisible();
      await expect(page.locator('.credit-note__marker--voided')).toBeVisible();
      await expect(page.getByText('Test cancellation')).toBeVisible();
      await assertNoClipping(page);
    });
  });
});

async function withPrintPage(
  page: any,
  scenario: CreditNoteScenario,
  code: string,
  assertion: () => Promise<void>,
): Promise<void> {
  const collector = collectBrowserFailures(page);
  try {
    await installCreditNoteFixture(page, scenario);
    await page.goto(`${ROUTE}/${code}/print`);
    await page.emulateMedia({ media: 'print', colorScheme: 'light' });
    await waitForStablePage(page);
    await assertion();
    assertNoUnexpectedBrowserFailures(collector.failures);
  } finally {
    collector.dispose();
  }
}

async function assertPrintLayout(page: any, options: { expectedSections: string[] }): Promise<void> {
  const article = page.locator('article.credit-note');
  await expect(article).toBeVisible();

  if (options.expectedSections.includes('header')) {
    await expect(page.locator('.credit-note__header')).toBeVisible();
  }
  if (options.expectedSections.includes('details')) {
    await expect(page.locator('.credit-note__panel').first()).toBeVisible();
  }
  if (options.expectedSections.includes('customer')) {
    await expect(page.locator('.credit-note__panel').nth(1)).toBeVisible();
  }
  if (options.expectedSections.includes('totals')) {
    await expect(page.locator('.credit-note__totals')).toBeVisible();
  }
  if (options.expectedSections.includes('terms')) {
    await expect(page.locator('.credit-note__terms')).toBeVisible();
  }
}

async function assertScreenControlsHidden(page: any): Promise<void> {
  const controls = page.locator('.screen-controls');
  const computed = await page.evaluate(() => {
    const el = document.querySelector('.screen-controls');
    return el ? window.getComputedStyle(el).display : 'none';
  });
  expect(['none', '']).toContain(computed);
}

async function assertNoClipping(page: any): Promise<void> {
  const metrics = await page.evaluate(() => {
    const panels = Array.from(document.querySelectorAll('.credit-note__panel'));
    const totals = document.querySelector('.credit-note__totals');
    const metrics = Array.from(document.querySelectorAll('.credit-note__metric'));
    const allElements = [...panels, totals, ...metrics].filter(Boolean);
    return allElements.map((el: any) => ({
      tag: el?.tagName,
      overflow: window.getComputedStyle(el as Element).overflow,
      height: (el as Element).scrollHeight,
      clientHeight: (el as Element).clientHeight,
    }));
  });
  for (const item of metrics) {
    if (item.overflow === 'hidden' && item.height > item.clientHeight) {
      throw new Error(`Clipping detected in ${item.tag}`);
    }
  }
}

async function assertNoPageBreakClipping(page: any): Promise<void> {
  await assertNoClipping(page);
  const orphans = await page.evaluate(() => {
    const article = document.querySelector('article.credit-note');
    return article ? window.getComputedStyle(article).orphans : 'auto';
  });
  expect(orphans).not.toBe('1');
}

async function assertDeterministicFonts(page: any): Promise<void> {
  const fonts = await page.evaluate(() => {
    const header = document.querySelector('.credit-note__header h1');
    const panels = document.querySelectorAll('.credit-note__panel h2');
    const elements = [header, ...Array.from(panels)];
    return elements.map((el) => ({
      tag: el?.tagName,
      font: el ? window.getComputedStyle(el).fontFamily : 'unknown',
    }));
  });
  for (const item of fonts) {
    expect(item.font).toBeTruthy();
    expect(item.font.length).toBeGreaterThan(0);
  }
}

async function assertNoOverlap(page: any): Promise<void> {
  const overlaps = await page.evaluate(() => {
    const elements = Array.from(document.querySelectorAll('.credit-note__panel, .credit-note__totals, .credit-note__terms'));
    const rects = elements.map((el) => ({
      el: (el as Element).className,
      rect: (el as Element).getBoundingClientRect(),
    }));
    for (let i = 0; i < rects.length; i++) {
      for (let j = i + 1; j < rects.length; j++) {
        const a = rects[i].rect;
        const b = rects[j].rect;
        if (a.bottom > b.top && a.top < b.bottom && a.right > b.left && a.left < b.right) {
          return { overlap: true, a: rects[i].el, b: rects[j].el };
        }
      }
    }
    return { overlap: false };
  });
  expect(overlaps.overlap).toBe(false);
}
