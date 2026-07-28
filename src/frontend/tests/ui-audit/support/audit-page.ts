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
  readonly ignoreResponse?: (response: Response) => boolean;
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
    if (response.status() >= 400 && !options.ignoreResponse?.(response)) {
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
  readonly authenticated?: boolean;
  readonly locale?: string;
  readonly accounts?: readonly MockBankAccount[];
  readonly bankAccountsState?: 'ready' | 'loading' | 'error';
  readonly bankAccountErrorStatus?: number;
}

export interface MockBankAccount {
  readonly id: string;
  readonly bankName: string;
  readonly accountNumber: string;
  readonly accountType: string | null;
  readonly ifscCode: string | null;
  readonly accountHolderName: string | null;
}

const DEFAULT_BANK_ACCOUNTS: readonly MockBankAccount[] = [
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

export async function mockExternalRequests(
  page: Page,
  options: MockExternalRequestsOptions = {},
): Promise<void> {
  if (options.authenticated) {
    await page.addInitScript(
      ({ locale }) => {
        localStorage.clear();
        sessionStorage.clear();
        localStorage.setItem(
          'inventory.auth.session.local',
          JSON.stringify({
            accessToken: 'ui-audit-owner-access',
            refreshToken: 'ui-audit-owner-refresh',
            accessTokenExpiresAt: '2099-01-01T00:00:00.000Z',
            refreshTokenExpiresAt: '2099-02-01T00:00:00.000Z',
            rememberMe: true,
            user: {
              id: 'ui-audit-owner',
              email: 'owner@ui-audit.example',
              phoneNumber: '+919999999999',
              firstName: 'UI',
              lastName: 'Audit Owner',
              language: locale,
            },
            activeShopId: 'ui-audit-shop',
            shops: [
              {
                shopId: 'ui-audit-shop',
                shopName: 'UI Audit Shop',
                role: 'Owner',
                isDefault: true,
                lastUsedAt: '2099-01-01T00:00:00.000Z',
              },
            ],
          }),
        );
        localStorage.setItem('inventory.preferences.language', locale);
      },
      { locale: options.locale ?? 'en-IN' },
    );
  }

  let accounts = [
    ...(options.accounts ?? (options.returnEmptyAccounts ? [] : DEFAULT_BANK_ACCOUNTS)),
  ];
  const listState = options.bankAccountsState ?? 'ready';

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

    if (route.request().method() === 'GET' && requestUrl.pathname === '/api/shops/me') {
      await fulfillJson(route, [
        {
          shopId: 'ui-audit-shop',
          shopName: 'UI Audit Shop',
          role: 'Owner',
          isDefault: true,
          lastUsedAt: '2099-01-01T00:00:00.000Z',
        },
      ]);
      return;
    }

    if (route.request().method() === 'GET' && requestUrl.pathname === '/api/shops/ui-audit-shop') {
      await fulfillJson(route, {
        shopId: 'ui-audit-shop',
        name: 'UI Audit Shop',
        address: '123 Deterministic Audit Avenue',
        city: 'Bengaluru',
        state: 'Karnataka',
        pincode: '560001',
        contactPerson: 'UI Audit Owner',
        mobileNumber: '+919999999999',
        gstNumber: '29ABCDE1234F1Z5',
        bankName: 'Audit Bank',
        bankAccountNumber: '1234567890',
        bankAccountType: 'Current',
        ifscCode: 'AUDT0000001',
        accountHolderName: 'UI Audit Shop',
        logoUrl: null,
        members: [],
      });
      return;
    }

    if (route.request().method() === 'GET' && requestUrl.pathname === '/api/ping') {
      await fulfillJson(route, { serverTime: '2099-01-01T00:00:00.000Z' });
      return;
    }

    if (route.request().method() === 'GET' && requestUrl.pathname === '/api/items/stream') {
      await route.fulfill({ status: 200, contentType: 'text/event-stream', body: '' });
      return;
    }

    if (requestUrl.pathname === '/api/bank-accounts') {
      if (route.request().method() === 'GET') {
        if (listState === 'loading') await delay(1_000);
        if (listState === 'error') {
          await fulfillBankAccountError(route, options.bankAccountErrorStatus ?? 503);
          return;
        }
        await fulfillJson(route, accounts);
        return;
      }
      if (route.request().method() === 'POST') {
        const account = createAccount(route.request(), accounts.length + 1);
        accounts = [...accounts, account];
        await fulfillJson(route, account);
        return;
      }
    }

    const accountId = requestUrl.pathname.match(/^\/api\/bank-accounts\/([^/]+)$/)?.[1];
    if (accountId && route.request().method() === 'PUT') {
      const payload = route.request().postDataJSON() as Partial<MockBankAccount>;
      const account = {
        ...accounts.find((item) => item.id === accountId),
        ...payload,
        id: accountId,
      } as MockBankAccount;
      accounts = accounts.map((item) => (item.id === accountId ? account : item));
      await fulfillJson(route, account);
      return;
    }
    if (accountId && route.request().method() === 'DELETE') {
      accounts = accounts.filter((item) => item.id !== accountId);
      await route.fulfill({ status: 204 });
      return;
    }
    if (requestUrl.pathname.startsWith('/api/bank-accounts/')) {
      await route.fulfill({ status: 404, contentType: 'application/json', body: '{}' });
      return;
    }

    await route.fulfill({ status: 200, contentType: 'text/plain', body: '' });
  });
}

function createAccount(request: Request, sequence: number): MockBankAccount {
  const payload = request.postDataJSON() as Omit<MockBankAccount, 'id'>;
  return { ...payload, id: `created-${sequence}` };
}

async function fulfillBankAccountError(
  route: import('@playwright/test').Route,
  status: number,
): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify({ title: 'BankAccounts.LoadFailed' }),
  });
}

async function fulfillJson(route: import('@playwright/test').Route, body: unknown): Promise<void> {
  await route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(body),
  });
}

async function delay(milliseconds: number): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, milliseconds));
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
