import type { Page, Route } from '@playwright/test';

import type { CreditNotePrintDto } from '../../../src/app/features/sales/services/sale.models';
import {
  createShellScenario,
  installShellFixture,
  type ShellScenario,
  type ShellScenarioOptions,
} from './shell.fixture';

const API_BASE = 'http://localhost:5277/api';

export type CreditNoteApiState = 'ready' | 'loading' | 'error';

export interface CreditNoteScenario {
  readonly shell: ShellScenario;
  readonly creditNotes: readonly CreditNotePrintDto[];
  readonly apiState: CreditNoteApiState;
}

export interface CreditNoteScenarioOptions extends ShellScenarioOptions {
  readonly creditNotes?: readonly CreditNotePrintDto[];
  readonly apiState?: CreditNoteApiState;
}

export const CREDIT_NOTE_STATUSES: readonly CreditNotePrintDto[] = [
  {
    creditNoteId: 'cn-active',
    code: 'CN-2026-001',
    status: 'Active',
    isUsable: true,
    originalAmount: 500,
    availableBalance: 350,
    issuedAt: '2026-06-01T10:00:00Z',
    expiresAt: '2026-09-01T00:00:00Z',
    saleId: 'sale-1',
    invoiceNumber: 'INV-2026-001',
    saleReturnId: 'return-1',
    returnNumber: 'RET-2026-001',
    customerDisplayName: 'Test Customer',
    reason: 'Defective item return',
    voidReason: null,
  },
  {
    creditNoteId: 'cn-expired',
    code: 'CN-2026-002',
    status: 'Expired',
    isUsable: false,
    originalAmount: 250,
    availableBalance: 0,
    issuedAt: '2026-03-01T10:00:00Z',
    expiresAt: '2026-06-01T00:00:00Z',
    saleId: 'sale-2',
    invoiceNumber: 'INV-2026-002',
    saleReturnId: 'return-2',
    returnNumber: 'RET-2026-002',
    customerDisplayName: null,
    reason: 'Partial credit',
    voidReason: null,
  },
];

export const DENSE_CREDIT_NOTE: CreditNotePrintDto = {
  creditNoteId: 'cn-dense',
  code: 'CN-2026-DENSE-001',
  status: 'Active',
  isUsable: true,
  originalAmount: 1250.75,
  availableBalance: 890.50,
  issuedAt: '2026-07-01T10:00:00Z',
  expiresAt: '2026-12-31T00:00:00Z',
  saleId: 'sale-dense',
  invoiceNumber: 'INV-2026-DENSE-001',
  saleReturnId: 'return-dense',
  returnNumber: 'RET-2026-DENSE-001',
  customerDisplayName: 'Very Long Customer Name With Deterministic Audit Value For Dense Testing',
  reason: 'Multiple items return including damaged packaging, incorrect quantity, and quality issues',
  voidReason: null,
};

export const LARGE_VALUE_CREDIT_NOTE: CreditNotePrintDto = {
  creditNoteId: 'cn-large',
  code: 'CN-2026-LARGE-001',
  status: 'Active',
  isUsable: true,
  originalAmount: 99999.99,
  availableBalance: 75000.00,
  issuedAt: '2026-01-01T10:00:00Z',
  expiresAt: '2027-01-01T00:00:00Z',
  saleId: 'sale-large',
  invoiceNumber: 'INV-2026-LARGE-001',
  saleReturnId: 'return-large',
  returnNumber: 'RET-2026-LARGE-001',
  customerDisplayName: 'Premium Customer Account',
  reason: 'Bulk return - wholesale adjustment',
  voidReason: null,
};

export function createCreditNoteScenario(
  options: CreditNoteScenarioOptions = {},
): CreditNoteScenario {
  return {
    shell: createShellScenario(options),
    creditNotes: options.creditNotes ?? CREDIT_NOTE_STATUSES,
    apiState: options.apiState ?? 'ready',
  };
}

export async function installCreditNoteFixture(
  page: Page,
  scenario: CreditNoteScenario,
): Promise<void> {
  await installShellFixture(page, scenario.shell);
  const state: CreditNoteFixtureState = {
    apiState: scenario.apiState,
    creditNotes: new Map(scenario.creditNotes.map((cn) => [cn.code, cn])),
  };
  await page.route(`${API_BASE}/credit-notes**`, (route) =>
    handleCreditNoteRoute(route, state),
  );
}

interface CreditNoteFixtureState {
  readonly apiState: CreditNoteApiState;
  readonly creditNotes: Map<string, CreditNotePrintDto>;
}

function handleCreditNoteRoute(route: Route, state: CreditNoteFixtureState): Promise<void> {
  if (state.apiState === 'loading') {
    return new Promise(() => undefined);
  }
  if (state.apiState === 'error') {
    return fulfillJson(route, { title: 'CreditNote.LoadFailed' }, 503);
  }

  const url = new URL(route.request().url());
  const segments = url.pathname.split('/').filter(Boolean);
  const pathIndex = segments.indexOf('credit-notes');
  if (pathIndex === -1 || pathIndex + 1 >= segments.length) {
    return fulfillJson(route, { title: 'CreditNote.CodeRequired' }, 400);
  }

  const code = decodeURIComponent(segments[pathIndex + 1]);
  const creditNote = state.creditNotes.get(code);
  return fulfillJson(route, creditNote ?? { title: 'CreditNote.NotFound' }, creditNote ? 200 : 404);
}

async function fulfillJson(route: Route, body: unknown, status = 200): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  });
}
