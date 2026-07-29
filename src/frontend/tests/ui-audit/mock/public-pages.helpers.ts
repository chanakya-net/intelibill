import { expect, type BrowserContext, type Page } from '@playwright/test';

import { type SupportedLanguage } from '../../../src/app/core/i18n/language.constants';
import { setStoredLanguage } from '../fixtures/auth.fixture';
import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  mockExternalRequests,
  waitForStablePage,
  type BrowserFailure,
} from '../support/audit-page';
import { type AUDIT_VIEWPORTS } from '../support/layout-assertions';

type Viewport = (typeof AUDIT_VIEWPORTS)[number];

interface ExpectedResponse {
  readonly status: number;
  readonly url: string;
}

export async function auditPublicPage(
  page: Page,
  url: string,
  configure: (page: Page) => Promise<void> = async () => {},
  verify: (page: Page) => Promise<void> = async () => {},
  expectedResponse?: ExpectedResponse,
): Promise<void> {
  const collector = collectBrowserFailures(page);

  try {
    await mockExternalRequests(page);
    await configure(page);
    await page.goto(url);
    await waitForStablePage(page);
    await verify(page);
    assertNoUnexpectedBrowserFailures(filterExpectedResponse(collector.failures, expectedResponse));
  } finally {
    collector.dispose();
  }
}

export async function isolatedLocalePage(
  context: BrowserContext,
  language: SupportedLanguage,
): Promise<Page> {
  const page = await context.newPage();
  await setStoredLanguage(page, language);
  return page;
}

export async function assertAuthLayout(page: Page, viewport: Viewport): Promise<void> {
  const metrics = await page.evaluate(() => {
    const rect = (selector: string) => document.querySelector<HTMLElement>(selector)?.getBoundingClientRect();
    const card = rect('.auth-card');
    const form = rect('.auth-card form');
    const language = rect('#language');
    const panel = document.querySelector<HTMLElement>('.visual-panel');
    const stage = document.querySelector<HTMLElement>('.auth-stage');

    return {
      documentWidth: document.documentElement.scrollWidth,
      card: card && { left: card.left, right: card.right, top: card.top, bottom: card.bottom },
      form: form && { left: form.left, right: form.right },
      language: language && { left: language.left, right: language.right, top: language.top, bottom: language.bottom },
      panelDisplay: panel ? getComputedStyle(panel).display : null,
      stageColumns: stage ? getComputedStyle(stage).gridTemplateColumns : null,
    };
  });

  expect(metrics.documentWidth).toBeLessThanOrEqual(viewport.width + 1);
  expect(metrics.card).not.toBeNull();
  expect(metrics.form).not.toBeNull();
  expect(metrics.language).not.toBeNull();
  expect(metrics.card!.left).toBeGreaterThanOrEqual(0);
  expect(metrics.card!.right).toBeLessThanOrEqual(viewport.width + 1);
  expect(metrics.form!.left).toBeGreaterThanOrEqual(metrics.card!.left);
  expect(metrics.form!.right).toBeLessThanOrEqual(metrics.card!.right);
  expect(metrics.language!.left).toBeGreaterThanOrEqual(metrics.card!.left);
  expect(metrics.language!.right).toBeLessThanOrEqual(metrics.card!.right);
  expect(metrics.language!.top).toBeGreaterThanOrEqual(metrics.card!.top);
  expect(metrics.language!.bottom).toBeLessThanOrEqual(metrics.card!.bottom);

  if (viewport.name === 'mobile') {
    expect(metrics.panelDisplay).toBe('none');
    expect(metrics.stageColumns).not.toContain(' ');
    return;
  }

  expect(metrics.card!.top).toBeGreaterThanOrEqual(0);
  expect(metrics.card!.bottom).toBeLessThanOrEqual(viewport.height + 1);
  expect(metrics.panelDisplay).not.toBe('none');
  expect(metrics.stageColumns).toContain(' ');
}

export async function assertCallbackLayout(page: Page, viewport: Viewport): Promise<void> {
  const metrics = await page.evaluate(() => {
    const card = document.querySelector<HTMLElement>('.auth-card')?.getBoundingClientRect();
    return {
      documentWidth: document.documentElement.scrollWidth,
      card: card && { left: card.left, right: card.right, top: card.top, bottom: card.bottom },
    };
  });

  expect(metrics.documentWidth).toBeLessThanOrEqual(viewport.width + 1);
  expect(metrics.card).not.toBeNull();
  expect(metrics.card!.left).toBeGreaterThanOrEqual(0);
  expect(metrics.card!.right).toBeLessThanOrEqual(viewport.width + 1);
  expect(metrics.card!.top).toBeGreaterThanOrEqual(0);
  expect(metrics.card!.bottom).toBeLessThanOrEqual(viewport.height + 1);
}

export async function assertAuthenticatedDestination(page: Page): Promise<void> {
  await page.waitForURL('/sales');
  await expect(page.locator('.app-shell')).toBeVisible();
  await expect(page.locator('.shell-main')).toBeVisible();
}

export async function assertActiveLocale(page: Page, language: SupportedLanguage): Promise<void> {
  await expect(page.locator('#language')).toHaveValue(language);
  await expect(page.locator('.auth-card h2')).not.toContainText('auth.');
}

export async function fillRegisterForm(page: Page): Promise<void> {
  await page.locator('#firstName').fill('Audit');
  await page.locator('#lastName').fill('User');
  await page.locator('#email').fill('audit@example.com');
  await page.locator('#phoneNumber').fill('9800000000');
  await page.locator('#password').fill('longenough1');
  await page.locator('#confirmPassword').fill('longenough1');
}

export async function fillResetForm(page: Page): Promise<void> {
  await page.locator('#password').fill('longenough1');
  await page.locator('#confirmPassword').fill('longenough1');
}

export function expectedError(url: string, status: number): ExpectedResponse {
  return { url, status };
}

function filterExpectedResponse(
  failures: readonly BrowserFailure[],
  expected?: ExpectedResponse,
): readonly BrowserFailure[] {
  const matchedExpectedResponse = failures.some(
    (failure) =>
      expected &&
      failure.kind === 'response' &&
      failure.url === expected.url &&
      failure.message === `HTTP ${expected.status}`,
  );

  return failures.filter(
    (failure) =>
      !expected ||
      !isExpectedFailure(failure, expected, matchedExpectedResponse),
  );
}

function isExpectedFailure(
  failure: BrowserFailure,
  expected: ExpectedResponse,
  matchedExpectedResponse: boolean,
): boolean {
  if (failure.kind === 'response') {
    return failure.url === expected.url && failure.message === `HTTP ${expected.status}`;
  }

  return matchedExpectedResponse &&
    failure.kind === 'console' &&
    failure.message === `Failed to load resource: the server responded with a status of ${expected.status} (${statusText(expected.status)})`;
}

function statusText(status: number): string {
  return {
    401: 'Unauthorized',
    409: 'Conflict',
    429: 'Too Many Requests',
    500: 'Internal Server Error',
  }[status] ?? 'Error';
}
