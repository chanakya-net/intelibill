import { expect, type Locator, type Page } from '@playwright/test';

import type { BrowserFailure } from '../support/audit-page';

export function filterDeclaredBatchFailures(
  failures: readonly BrowserFailure[],
  saveUrl: string,
): BrowserFailure[] {
  return failures.filter((failure) =>
    !(
      failure.kind === 'response' && failure.message === 'HTTP 503' && failure.url === saveUrl
    ) && !(failure.kind === 'console' && failure.message.includes('status of 503')),
  );
}

export function pendingRowsFor(page: Page, projectName: string): Locator {
  const selector = projectName === 'chromium-mobile'
    ? '.pending-row-card'
    : '.desktop-pending-table tbody tr:not(:has(.empty-state))';
  return page.locator(selector);
}

export function pendingCollectionFor(page: Page, projectName: string): Locator {
  return page.locator(projectName === 'chromium-mobile' ? '.mobile-rows-list' : '.p-datatable-table-container');
}

export async function assertFieldsFitViewport(page: Page, selector: string): Promise<void> {
  const outsideViewport = await page.locator(selector).evaluateAll((elements) =>
    elements.some((element) => {
      const box = element.getBoundingClientRect();
      return box.left < 0 || box.right > innerWidth;
    }),
  );
  expect(outsideViewport).toBe(false);
}

export async function assertOverlayFitsViewport(overlay: Locator): Promise<void> {
  const overlayState = await overlay.evaluate((element) => {
    const box = element.getBoundingClientRect();
    const topElement = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
    return {
      fits: box.left >= 0 && box.right <= innerWidth && box.top >= 0 && box.bottom <= innerHeight,
      topmost: topElement === element || element.contains(topElement),
    };
  });
  expect(overlayState.fits).toBe(true);
  expect(overlayState.topmost).toBe(true);
}
