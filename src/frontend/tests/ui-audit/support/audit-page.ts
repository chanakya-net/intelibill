import { access, mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';

import type { ConsoleMessage, Page, Request, Response } from '@playwright/test';
import type { SupplierMockOptions } from './suppliers-mock';
import { getSupplierDefaults, createSupplierRouteHandler } from './suppliers-mock';

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
  readonly returnEmptyCustomers?: boolean;
  readonly customers?: readonly MockCustomer[];
  readonly customersState?: 'ready' | 'loading' | 'error';
  readonly customersErrorStatus?: number;
  readonly customerAccounts?: Record<string, MockCustomerAccount>;
  readonly customerAccountError?: number;
  readonly customerPaymentError?: number;
  readonly supplierMock?: SupplierMockOptions;
}

export interface MockCustomer {
  readonly customerId: string;
  readonly name: string;
  readonly phoneNumber: string;
  readonly address: string | null;
  readonly isActive: boolean;
  readonly creditLimit: number;
  readonly purchaseCount: number;
  readonly lifetimeRevenue: number;
  readonly currentMonthRevenue: number;
  readonly outstandingDue?: number;
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

const DEFAULT_CUSTOMERS: readonly MockCustomer[] = [
  {
    customerId: '1',
    name: 'John Doe',
    phoneNumber: '+91-9876543210',
    address: '123 Main St, Bengaluru',
    isActive: true,
    creditLimit: 50000,
    purchaseCount: 10,
    lifetimeRevenue: 150000,
    currentMonthRevenue: 5000,
    outstandingDue: 2000,
  },
  {
    customerId: '2',
    name: 'Jane Smith',
    phoneNumber: '+91-9123456789',
    address: '456 Oak Avenue, Pune',
    isActive: true,
    creditLimit: 75000,
    purchaseCount: 25,
    lifetimeRevenue: 350000,
    currentMonthRevenue: 12000,
    outstandingDue: 5000,
  },
];

const DEFAULT_CUSTOMER_ACCOUNTS: Readonly<Record<string, MockCustomerAccount>> = {
  '1': {
    customerId: '1',
    name: 'John Doe',
    phoneNumber: '+91-9876543210',
    outstandingDue: 2000,
    sales: [
      {
        saleId: 'sale-1',
        invoiceNumber: 'INV-001',
        paymentMethod: 0,
        soldAt: '2025-01-15T10:30:00Z',
        paidAmount: 8000,
        dueAmount: 2000,
        totalAmount: 10000,
      },
      {
        saleId: 'sale-2',
        invoiceNumber: 'INV-002',
        paymentMethod: 1,
        soldAt: '2025-02-20T14:45:00Z',
        paidAmount: 15000,
        dueAmount: 0,
        totalAmount: 15000,
      },
    ],
    ledgerEntries: [
      {
        entryId: 'ledger-1',
        saleId: 'sale-1',
        entryType: 0,
        amount: 10000,
        entryDate: '2025-01-15T10:30:00Z',
        notes: 'Initial sale',
        runningBalance: 10000,
      },
      {
        entryId: 'ledger-2',
        saleId: null,
        entryType: 1,
        amount: 8000,
        entryDate: '2025-01-20T11:00:00Z',
        notes: 'Partial payment',
        runningBalance: 2000,
      },
    ],
    paymentHistory: [
      {
        entryId: 'payment-1',
        saleId: null,
        entryType: 1,
        amount: 8000,
        entryDate: '2025-01-20T11:00:00Z',
        notes: 'Bank transfer',
        runningBalance: 2000,
      },
    ],
  },
  '2': {
    customerId: '2',
    name: 'Jane Smith',
    phoneNumber: '+91-9123456789',
    outstandingDue: 5000,
    sales: [
      {
        saleId: 'sale-3',
        invoiceNumber: 'INV-003',
        paymentMethod: 0,
        soldAt: '2025-02-10T09:15:00Z',
        paidAmount: 20000,
        dueAmount: 5000,
        totalAmount: 25000,
      },
    ],
    ledgerEntries: [
      {
        entryId: 'ledger-3',
        saleId: 'sale-3',
        entryType: 0,
        amount: 25000,
        entryDate: '2025-02-10T09:15:00Z',
        notes: 'Bulk order',
        runningBalance: 25000,
      },
      {
        entryId: 'ledger-4',
        saleId: null,
        entryType: 1,
        amount: 20000,
        entryDate: '2025-02-15T15:30:00Z',
        notes: 'Advance payment',
        runningBalance: 5000,
      },
    ],
    paymentHistory: [
      {
        entryId: 'payment-2',
        saleId: null,
        entryType: 1,
        amount: 20000,
        entryDate: '2025-02-15T15:30:00Z',
        notes: 'Cheque deposit',
        runningBalance: 5000,
      },
    ],
  },
};

export interface MockCustomerAccount {
  readonly customerId: string;
  readonly name: string;
  readonly phoneNumber: string;
  readonly outstandingDue: number;
  readonly sales: readonly MockCustomerAccountSale[];
  readonly ledgerEntries: readonly MockCustomerLedgerEntry[];
  readonly paymentHistory: readonly MockCustomerLedgerEntry[];
}

export interface MockCustomerAccountSale {
  readonly saleId: string;
  readonly invoiceNumber: string;
  readonly paymentMethod: number;
  readonly soldAt: string;
  readonly paidAmount: number;
  readonly dueAmount: number;
  readonly totalAmount: number;
}

export interface MockCustomerLedgerEntry {
  readonly entryId: string;
  readonly saleId: string | null;
  readonly entryType: number;
  readonly amount: number;
  readonly entryDate: string;
  readonly notes: string | null;
  readonly runningBalance: number;
}

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

  let customers = [
    ...(options.customers ?? (options.returnEmptyCustomers ? [] : DEFAULT_CUSTOMERS)),
  ];
  const customersListState = options.customersState ?? 'ready';
  let customerAccounts = cloneCustomerAccounts({
    ...DEFAULT_CUSTOMER_ACCOUNTS,
    ...options.customerAccounts,
  });

  const { suppliers, supplierDetails } = getSupplierDefaults(options.supplierMock ?? {});
  const mutableSupplierState = { suppliers, supplierDetails };
  const handleSupplierRoute = createSupplierRouteHandler(mutableSupplierState, options.supplierMock ?? {});

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
      requestUrl.port !== '5277';

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

    if (requestUrl.pathname === '/api/customers') {
      if (route.request().method() === 'GET') {
        if (customersListState === 'loading') await delay(1_000);
        if (customersListState === 'error') {
          await fulfillCustomerError(route, options.customersErrorStatus ?? 503);
          return;
        }
        await fulfillJson(route, customers);
        return;
      }
      if (route.request().method() === 'POST') {
        const customer = createCustomer(route.request(), customers.length + 1);
        customers = [...customers, customer];
        await fulfillJson(route, customer);
        return;
      }
    }

    const customerId = requestUrl.pathname.match(/^\/api\/customers\/([^/]+)$/)?.[1];
    if (customerId && route.request().method() === 'PUT') {
      const payload = route.request().postDataJSON() as Partial<MockCustomer>;
      const customer = {
        ...customers.find((item) => item.customerId === customerId),
        ...payload,
        customerId,
      } as MockCustomer;
      customers = customers.map((item) => (item.customerId === customerId ? customer : item));
      await fulfillJson(route, customer);
      return;
    }
    if (customerId && route.request().method() === 'DELETE') {
      customers = customers.filter((item) => item.customerId !== customerId);
      await route.fulfill({ status: 204 });
      return;
    }

    const accountMatch = requestUrl.pathname.match(/^\/api\/customers\/([^/]+)\/account$/);
    if (accountMatch?.[1]) {
      if (options.customerAccountError) {
        await fulfillCustomerAccountError(route, options.customerAccountError);
        return;
      }
      const id = accountMatch[1];
      const account = customerAccounts[id];
      if (account) {
        await fulfillJson(route, account);
        return;
      }
    }

    const paymentMatch = requestUrl.pathname.match(/^\/api\/customers\/([^/]+)\/payments$/);
    if (paymentMatch?.[1] && route.request().method() === 'POST') {
      const customerId = paymentMatch[1];
      if (options.customerPaymentError) {
        await route.fulfill({
          status: options.customerPaymentError,
          contentType: 'application/json',
          body: JSON.stringify({ title: 'Payment.Failed' }),
        });
        return;
      }
      const account = customerAccounts[customerId];
      if (account && account.ledgerEntries.length > 0) {
        const payload = route.request().postDataJSON() as { amount?: number };
        const newEntry: MockCustomerLedgerEntry = {
          entryId: `payment-${Date.now()}`,
          saleId: null,
          entryType: 1,
          amount: payload.amount ?? 0,
          entryDate: new Date().toISOString(),
          notes: 'Payment submitted',
          runningBalance: Math.max(0, account.outstandingDue - (payload.amount ?? 0)),
        };
        customerAccounts = {
          ...customerAccounts,
          [customerId]: {
            ...account,
            outstandingDue: newEntry.runningBalance,
            ledgerEntries: [...account.ledgerEntries, newEntry],
            paymentHistory: [...account.paymentHistory, newEntry],
          },
        };
        await fulfillJson(route, newEntry);
        return;
      }
    }

    if (
      requestUrl.pathname.startsWith('/api/customers/') &&
      !requestUrl.pathname.includes('/account') &&
      !requestUrl.pathname.includes('/payments')
    ) {
      await route.fulfill({ status: 404, contentType: 'application/json', body: '{}' });
      return;
    }

    if (await handleSupplierRoute(route)) {
      return;
    }

    await route.fulfill({ status: 200, contentType: 'text/plain', body: '' });
  });
}

function createAccount(request: Request, sequence: number): MockBankAccount {
  const payload = request.postDataJSON() as Omit<MockBankAccount, 'id'>;
  return { ...payload, id: `created-${sequence}` };
}

function createCustomer(request: Request, sequence: number): MockCustomer {
  const payload = request.postDataJSON() as Omit<MockCustomer, 'customerId'>;
  return { ...payload, customerId: `created-${sequence}` };
}

function cloneCustomerAccounts(
  accounts: Readonly<Record<string, MockCustomerAccount>>,
): Record<string, MockCustomerAccount> {
  return Object.fromEntries(
    Object.entries(accounts).map(([customerId, account]) => [
      customerId,
      {
        ...account,
        sales: [...account.sales],
        ledgerEntries: [...account.ledgerEntries],
        paymentHistory: [...account.paymentHistory],
      },
    ]),
  );
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

async function fulfillCustomerError(
  route: import('@playwright/test').Route,
  status: number,
): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify({ title: 'Customers.LoadFailed' }),
  });
}

async function fulfillCustomerAccountError(
  route: import('@playwright/test').Route,
  status: number,
): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify({ detail: 'Unable to load customer account right now.' }),
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
