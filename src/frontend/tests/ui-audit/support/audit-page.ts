import { access, mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';

import type { ConsoleMessage, Page, Request, Response } from '@playwright/test';

export interface BrowserFailure {
  readonly kind: 'console' | 'pageerror' | 'request' | 'response';
  readonly message: string;
  readonly url?: string;
}

export interface FailureCollector {
  readonly failures: BrowserFailure[];
  dispose(): void;
}

export interface FailureCollectorOptions {
  readonly ignoreConsole?: (message: string) => boolean;
}

export function isBaselineUpdateEnabled(environment: NodeJS.ProcessEnv): boolean {
  return environment.UI_AUDIT_UPDATE_BASELINE === '1';
}

export function collectBrowserFailures(
  page: Page,
  options: FailureCollectorOptions = {},
): FailureCollector {
  const failures: BrowserFailure[] = [];
  const onConsole = (message: ConsoleMessage) => {
    if (message.type() === 'error' && !options.ignoreConsole?.(message.text())) {
      failures.push({ kind: 'console', message: message.text() });
    }
  };
  const onPageError = (error: Error) =>
    failures.push({ kind: 'pageerror', message: error.message });
  const onRequestFailed = (request: Request) =>
    failures.push({
      kind: 'request',
      message: request.failure()?.errorText ?? 'request failed',
      url: request.url(),
    });
  const onResponse = (response: Response) => {
    if (response.status() >= 400) {
      failures.push({
        kind: 'response',
        message: `HTTP ${response.status()}`,
        url: response.url(),
      });
    }
  };

  page.on('console', onConsole);
  page.on('pageerror', onPageError);
  page.on('requestfailed', onRequestFailed);
  page.on('response', onResponse);

  return {
    failures,
    dispose: () => {
      page.off('console', onConsole);
      page.off('pageerror', onPageError);
      page.off('requestfailed', onRequestFailed);
      page.off('response', onResponse);
    },
  };
}

export function assertNoUnexpectedBrowserFailures(failures: readonly BrowserFailure[]): void {
  if (failures.length > 0) {
    throw new Error(`unexpected browser failures: ${JSON.stringify(failures)}`);
  }
}

export interface MockExternalRequestsOptions {
  readonly returnEmptyAccounts?: boolean;
}

export async function mockExternalRequests(page: Page, options: MockExternalRequestsOptions = {}): Promise<void> {
  await page.routeWebSocket('ws://localhost:5277/**', (webSocket) => {
    webSocket.onMessage((message) => {
      if (typeof message === 'string' && message.includes('"protocol"')) {
        webSocket.send('{}\u001e');
      }
    });
  });

  await page.route('**/*', async (route) => {
    const requestUrl = new URL(route.request().url());
    const isLocalApp =
      (requestUrl.hostname === '127.0.0.1' || requestUrl.hostname === 'localhost') &&
      requestUrl.port === '4300';

    if (isLocalApp || requestUrl.protocol === 'data:') {
      await route.continue();
      return;
    }

    if (requestUrl.pathname.endsWith('/negotiate')) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          connectionId: 'ui-audit',
          connectionToken: 'ui-audit',
          negotiateVersion: 1,
          availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text'] }],
        }),
      });
      return;
    }

    // Mock bank accounts API
    if (requestUrl.pathname.includes('/api/bank-accounts')) {
      const accounts = options.returnEmptyAccounts ? [] : [
        {
          id: '1',
          bankName: 'HDFC Bank',
          accountNumber: '1234567890123456',
          accountType: 'Savings',
          ifscCode: 'HDFC0001234',
          accountHolderName: 'John Doe',
        },
        {
          id: '2',
          bankName: 'ICICI Bank',
          accountNumber: '9876543210987654',
          accountType: 'Current',
          ifscCode: 'ICIC0005678',
          accountHolderName: 'Jane Smith',
        },
      ];

      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(accounts),
      });
      return;
    }

    await route.fulfill({ status: 200, contentType: 'text/plain', body: '' });
  });
}

export async function waitForStablePage(page: Page): Promise<void> {
  await page.waitForLoadState('domcontentloaded');
  await page.addStyleTag({
    content:
      '*, *::before, *::after { animation: none !important; transition: none !important; caret-color: transparent !important; }',
  });
  await page.evaluate(async () => {
    await document.fonts.ready;
    document.documentElement.classList.add('ui-audit-stable');
    window.scrollTo(0, 0);
  });
  await page.waitForTimeout(250);
}

export async function compareScreenshot(options: {
  readonly page: Page;
  readonly baselinePath: string;
  readonly updateBaseline: boolean;
}): Promise<{
  readonly status: 'updated' | 'matched' | 'stable-without-baseline';
  readonly bytes: Buffer;
}> {
  const bytes = await options.page.screenshot({ fullPage: true, animations: 'disabled' });

  if (options.updateBaseline) {
    await mkdir(dirname(options.baselinePath), { recursive: true });
    await writeFile(options.baselinePath, bytes);
    return { status: 'updated', bytes };
  }

  if (!(await fileExists(options.baselinePath))) {
    await options.page.waitForTimeout(250);
    const repeat = await options.page.screenshot({ fullPage: true, animations: 'disabled' });
    if (!bytes.equals(repeat)) {
      throw new Error('login screenshot is not stable across deterministic captures');
    }
    return { status: 'stable-without-baseline', bytes };
  }

  const expected = await readFile(options.baselinePath);
  if (!bytes.equals(expected)) {
    throw new Error(`login screenshot differs from approved baseline: ${options.baselinePath}`);
  }

  return { status: 'matched', bytes };
}

async function fileExists(path: string): Promise<boolean> {
  try {
    await access(path);
    return true;
  } catch {
    return false;
  }
}
