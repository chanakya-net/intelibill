import type { Page, Route, Request } from '@playwright/test';

export interface MockSupplier {
  readonly supplierId: string;
  readonly name: string;
  readonly contactPersonName: string | null;
  readonly contactPersonPhone: string | null;
  readonly address: string;
  readonly city: string;
  readonly state: string;
  readonly pin: string;
  readonly gstNumber?: string | null;
  readonly isSystem: boolean;
  readonly isActive: boolean;
  readonly isPreferred: boolean;
  readonly balanceDue: number;
}

export interface MockSupplierDetail {
  readonly supplierId: string;
  readonly name: string;
  readonly contactPersonName: string | null;
  readonly contactPersonPhone: string | null;
  readonly balanceDue: number;
  readonly ledgerEntries: readonly MockSupplierLedgerEntry[];
  readonly paymentHistory: readonly MockSupplierLedgerEntry[];
}

export interface MockSupplierLedgerEntry {
  readonly id: string;
  readonly supplierId: string;
  readonly entryType: 'GOODS_RECEIVED' | 'PAYMENT_MADE' | 'RECORD_ADJUSTED' | number;
  readonly amount: number;
  readonly entryDate: string;
  readonly notes: string | null;
}

const DEFAULT_SUPPLIERS: readonly MockSupplier[] = [
  {
    supplierId: '1',
    name: 'Audit Supplier One',
    contactPersonName: 'Supplier Contact',
    contactPersonPhone: '+919876543210',
    address: '123 Supplier St, Bengaluru',
    city: 'Bengaluru',
    state: 'Karnataka',
    pin: '560001',
    gstNumber: '29SUPPLIER1234F1Z5',
    isSystem: false,
    isActive: true,
    isPreferred: false,
    balanceDue: 50000,
  },
  {
    supplierId: '2',
    name: 'Audit Supplier Two',
    contactPersonName: 'Another Contact',
    contactPersonPhone: '+919123456789',
    address: '456 Supplier Avenue, Pune',
    city: 'Pune',
    state: 'Maharashtra',
    pin: '411001',
    gstNumber: '27SUPPLIER2234F1Z5',
    isSystem: false,
    isActive: true,
    isPreferred: true,
    balanceDue: 75000,
  },
];

const DEFAULT_SUPPLIER_DETAILS: Readonly<Record<string, MockSupplierDetail>> = {
  '1': {
    supplierId: '1',
    name: 'Audit Supplier One',
    contactPersonName: 'Supplier Contact',
    contactPersonPhone: '+919876543210',
    balanceDue: 50000,
    ledgerEntries: [
      {
        id: 'ledger-1',
        supplierId: '1',
        entryType: 'GOODS_RECEIVED',
        amount: 50000,
        entryDate: '2025-01-15T10:30:00Z',
        notes: 'Purchase order received',
      },
      {
        id: 'ledger-2',
        supplierId: '1',
        entryType: 'PAYMENT_MADE',
        amount: 0,
        entryDate: '2025-01-20T11:00:00Z',
        notes: null,
      },
    ],
    paymentHistory: [
      {
        id: 'payment-1',
        supplierId: '1',
        entryType: 'PAYMENT_MADE',
        amount: 0,
        entryDate: '2025-01-20T11:00:00Z',
        notes: null,
      },
    ],
  },
  '2': {
    supplierId: '2',
    name: 'Audit Supplier Two',
    contactPersonName: 'Another Contact',
    contactPersonPhone: '+919123456789',
    balanceDue: 75000,
    ledgerEntries: [
      {
        id: 'ledger-3',
        supplierId: '2',
        entryType: 'GOODS_RECEIVED',
        amount: 75000,
        entryDate: '2025-02-10T09:15:00Z',
        notes: 'Bulk order',
      },
      {
        id: 'ledger-4',
        supplierId: '2',
        entryType: 'PAYMENT_MADE',
        amount: 0,
        entryDate: '2025-02-15T15:30:00Z',
        notes: null,
      },
    ],
    paymentHistory: [
      {
        id: 'payment-2',
        supplierId: '2',
        entryType: 'PAYMENT_MADE',
        amount: 0,
        entryDate: '2025-02-15T15:30:00Z',
        notes: null,
      },
    ],
  },
};

export interface SupplierMockOptions {
  readonly returnEmptySuppliers?: boolean;
  readonly suppliers?: readonly MockSupplier[];
  readonly suppliersState?: 'ready' | 'loading' | 'error';
  readonly suppliersErrorStatus?: number;
  readonly supplierDetails?: Record<string, MockSupplierDetail>;
  readonly supplierDetailError?: number;
  readonly supplierLedgerError?: number;
  readonly supplierPaymentError?: number;
}

export function getSupplierDefaults(
  options: SupplierMockOptions,
): {
  suppliers: MockSupplier[];
  supplierDetails: Record<string, MockSupplierDetail>;
  suppliersListState: 'ready' | 'loading' | 'error';
} {
  const suppliers = [
    ...(options.suppliers ?? (options.returnEmptySuppliers ? [] : DEFAULT_SUPPLIERS)),
  ];
  const suppliersListState = options.suppliersState ?? 'ready';
  const supplierDetails = cloneSupplierDetails({
    ...DEFAULT_SUPPLIER_DETAILS,
    ...options.supplierDetails,
  });
  return { suppliers, supplierDetails, suppliersListState };
}

export function cloneSupplierDetails(
  details: Readonly<Record<string, MockSupplierDetail>>,
): Record<string, MockSupplierDetail> {
  return Object.fromEntries(
    Object.entries(details).map(([supplierId, detail]) => [
      supplierId,
      {
        ...detail,
        ledgerEntries: [...detail.ledgerEntries],
        paymentHistory: [...detail.paymentHistory],
      },
    ]),
  );
}

export function createSupplier(request: Request, sequence: number): MockSupplier {
  const payload = request.postDataJSON() as Omit<MockSupplier, 'supplierId'>;
  return { ...payload, supplierId: `created-${sequence}` };
}

export async function fulfillSupplierError(
  route: Route,
  status: number,
): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify({ title: 'Suppliers.LoadFailed' }),
  });
}

async function fulfillSupplierDetailError(route: Route, status: number): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify({ detail: 'Unable to load supplier details right now.' }),
  });
}

async function fulfillSupplierLedgerError(route: Route, status: number): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify({ detail: 'Unable to load supplier ledger right now.' }),
  });
}

async function fulfillSupplierPaymentError(route: Route, status: number): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify({ detail: 'Unable to record payment right now.' }),
  });
}

async function fulfillJson(route: Route, body: unknown): Promise<void> {
  await route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(body),
  });
}

async function delay(milliseconds: number): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, milliseconds));
}

export function createSupplierRouteHandler(
  mutableState: {
    suppliers: MockSupplier[];
    supplierDetails: Record<string, MockSupplierDetail>;
  },
  options: SupplierMockOptions,
): (route: Route) => Promise<boolean> {
  const suppliersListState = options.suppliersState ?? 'ready';

  return async (route: Route) => {
    const requestUrl = new URL(route.request().url());

    if (requestUrl.pathname === '/api/suppliers') {
      if (route.request().method() === 'GET') {
        if (suppliersListState === 'loading') await delay(1_000);
        if (suppliersListState === 'error') {
          await fulfillSupplierError(route, options.suppliersErrorStatus ?? 503);
          return true;
        }
        await fulfillJson(route, mutableState.suppliers);
        return true;
      }
      if (route.request().method() === 'POST') {
        const supplier = createSupplier(route.request(), mutableState.suppliers.length + 1);
        mutableState.suppliers = [...mutableState.suppliers, supplier];
        await fulfillJson(route, supplier);
        return true;
      }
    }

    const supplierId = requestUrl.pathname.match(/^\/api\/suppliers\/([^/]+)$/)?.[1];
    if (supplierId && route.request().method() === 'PUT') {
      const payload = route.request().postDataJSON() as Partial<MockSupplier>;
      const supplier = {
        ...mutableState.suppliers.find((item) => item.supplierId === supplierId),
        ...payload,
        supplierId,
      } as MockSupplier;
      mutableState.suppliers = mutableState.suppliers.map((item) =>
        item.supplierId === supplierId ? supplier : item,
      );
      await fulfillJson(route, supplier);
      return true;
    }
    if (supplierId && route.request().method() === 'DELETE') {
      mutableState.suppliers = mutableState.suppliers.filter(
        (item) => item.supplierId !== supplierId,
      );
      await route.fulfill({ status: 204 });
      return true;
    }

    const supplierDetailPath = requestUrl.pathname.match(/^\/api\/suppliers\/([^/]+)\/detail$/)?.[1];
    if (supplierDetailPath) {
      if (route.request().method() === 'GET') {
        if (options.supplierDetailError) {
          await fulfillSupplierDetailError(route, options.supplierDetailError);
          return true;
        }
        const detail = mutableState.supplierDetails[supplierDetailPath];
        await fulfillJson(route, detail || { supplierId: supplierDetailPath });
        return true;
      }
    }

    const supplierLedgerPath = requestUrl.pathname.match(/^\/api\/suppliers\/([^/]+)\/ledger$/)?.[1];
    if (supplierLedgerPath) {
      if (route.request().method() === 'GET') {
        if (options.supplierLedgerError) {
          await fulfillSupplierLedgerError(route, options.supplierLedgerError);
          return true;
        }
        const detail = mutableState.supplierDetails[supplierLedgerPath];
        await fulfillJson(route, detail?.ledgerEntries || []);
        return true;
      }
    }

    const supplierPaymentPath = requestUrl.pathname.match(/^\/api\/suppliers\/([^/]+)\/payments$/)?.[1];
    if (supplierPaymentPath) {
      if (route.request().method() === 'POST') {
        if (options.supplierPaymentError) {
          await fulfillSupplierPaymentError(route, options.supplierPaymentError);
          return true;
        }
        const detail = mutableState.supplierDetails[supplierPaymentPath];
        if (detail) {
          const payload = route.request().postDataJSON() as {
            amount: number;
            paymentDate: string;
            notes: string | null;
          };
          const newEntry: MockSupplierLedgerEntry = {
            id: `payment-${Date.now()}`,
            supplierId: supplierPaymentPath,
            entryType: 'PAYMENT_MADE',
            amount: payload.amount,
            entryDate: payload.paymentDate,
            notes: payload.notes,
          };
          mutableState.supplierDetails[supplierPaymentPath] = {
            ...detail,
            balanceDue: Math.max(0, detail.balanceDue - payload.amount),
            ledgerEntries: [...detail.ledgerEntries, newEntry],
            paymentHistory: [...detail.paymentHistory, newEntry],
          };
          await fulfillJson(route, newEntry);
          return true;
        }
      }
    }
    return false;
  };
}
