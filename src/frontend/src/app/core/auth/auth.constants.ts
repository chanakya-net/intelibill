import { environment } from '../../../environments/environment';

export const API_BASE_URL = environment.apiBaseUrl;
export const HUB_BASE_URL = environment.hubBaseUrl;

export const AUTH_ENDPOINTS = {
  registerWithEmail: `${API_BASE_URL}/auth/register/email`,
  login: `${API_BASE_URL}/auth/login`,
  loginWithEmail: `${API_BASE_URL}/auth/login`,
  loginExternalInit: `${API_BASE_URL}/auth/login/external/init`,
  loginExternalCallback: `${API_BASE_URL}/auth/login/external/callback`,
  refreshToken: `${API_BASE_URL}/auth/token/refresh`,
  revokeToken: `${API_BASE_URL}/auth/token/revoke`,
  requestPasswordReset: `${API_BASE_URL}/auth/password-reset/request`,
  confirmPasswordReset: `${API_BASE_URL}/auth/password-reset/confirm`,
} as const;

export const SHOP_ENDPOINTS = {
  me: `${API_BASE_URL}/shops/me`,
  create: `${API_BASE_URL}/shops`,
  setDefault: `${API_BASE_URL}/shops/default`,
  details: (shopId: string) => `${API_BASE_URL}/shops/${shopId}`,
  update: (shopId: string) => `${API_BASE_URL}/shops/${shopId}`,
  bankDetails: (shopId: string) => `${API_BASE_URL}/shops/${shopId}/bank-details`,
  addBankAccount: (_: string) => `${API_BASE_URL}/bank-accounts`,
} as const;

export const USER_ENDPOINTS = {
  list: `${API_BASE_URL}/users`,
  add: `${API_BASE_URL}/users`,
  update: (userId: string) => `${API_BASE_URL}/users/${userId}`,
  me: `${API_BASE_URL}/users/me`,
  changePassword: `${API_BASE_URL}/users/me/change-password`,
} as const;

export const SUPPLIER_ENDPOINTS = {
  list: `${API_BASE_URL}/suppliers`,
  add: `${API_BASE_URL}/suppliers`,
  update: (supplierId: string) => `${API_BASE_URL}/suppliers/${supplierId}`,
  payments: (supplierId: string) => `${API_BASE_URL}/suppliers/${supplierId}/payments`,
} as const;

export const ITEM_ENDPOINTS = {
  list: `${API_BASE_URL}/items`,
  add: `${API_BASE_URL}/items`,
  stream: `${API_BASE_URL}/items/stream`,
  update: (itemId: string) => `${API_BASE_URL}/items/${itemId}`,
} as const;

export const INVENTORY_ENDPOINTS = {
  inbound: `${API_BASE_URL}/inventory/inbound`,
  inboundBatch: `${API_BASE_URL}/inventory/inbound/batch`,
  availableBatches: (searchTerm: string) => `${API_BASE_URL}/inventory/batches/available?searchTerm=${encodeURIComponent(searchTerm)}`,
} as const;

export const ITEM_BARCODE_ENDPOINTS = {
  generate: `${API_BASE_URL}/items/barcodes/generate`,
  printLabels: `${API_BASE_URL}/items/barcodes/labels`,
} as const;

export const CREDIT_NOTE_ENDPOINTS = {
  list: `${API_BASE_URL}/credit-notes`,
  detail: (code: string) => `${API_BASE_URL}/credit-notes/${encodeURIComponent(code)}`,
} as const;

export const SALE_ENDPOINTS = {
  list: `${API_BASE_URL}/sales`,
  record: `${API_BASE_URL}/sales`,
  preview: `${API_BASE_URL}/sales/preview`,
  detail: (saleId: string) => `${API_BASE_URL}/sales/${saleId}`,
  profitLoss: `${API_BASE_URL}/sales/profit-loss`,
  reserveInvoiceLease: `${API_BASE_URL}/sales/invoice-leases/reserve`,
  offlineSnapshotStream: `${API_BASE_URL}/sales/offline-snapshot/stream`,
  offlineSync: `${API_BASE_URL}/sales/offline-sync`,
  sellables: (searchTerm: string) => `${API_BASE_URL}/sales/sellables?searchTerm=${encodeURIComponent(searchTerm)}`,
  creditNoteByCode: (code: string) => CREDIT_NOTE_ENDPOINTS.detail(code),
} as const;

export const BANK_ACCOUNT_ENDPOINTS = {
  list: `${API_BASE_URL}/bank-accounts`,
  add: `${API_BASE_URL}/bank-accounts`,
  update: (id: string) => `${API_BASE_URL}/bank-accounts/${id}`,
  delete: (id: string) => `${API_BASE_URL}/bank-accounts/${id}`,
} as const;

export const EXPENSE_ENDPOINTS = {
  list: `${API_BASE_URL}/expenses`,
  record: `${API_BASE_URL}/expenses`,
  detail: (expenseId: string) => `${API_BASE_URL}/expenses/${expenseId}`,
  correct: (expenseId: string) => `${API_BASE_URL}/expenses/${expenseId}/correct`,
  categories: `${API_BASE_URL}/expenses/categories`,
} as const;

export const DISCOUNT_ENDPOINTS = {
  list: `${API_BASE_URL}/discounts`,
  create: `${API_BASE_URL}/discounts`,
  detail: (id: string) => `${API_BASE_URL}/discounts/${id}`,
  disable: (id: string) => `${API_BASE_URL}/discounts/${id}/disable`,
  preview: `${API_BASE_URL}/discounts/preview`,
} as const;

export const SERVICE_ENDPOINTS = {
  list: `${API_BASE_URL}/services`,
  add: `${API_BASE_URL}/services`,
  update: (serviceId: string) => `${API_BASE_URL}/services/${serviceId}`,
  activate: (serviceId: string) => `${API_BASE_URL}/services/${serviceId}/activate`,
  deactivate: (serviceId: string) => `${API_BASE_URL}/services/${serviceId}/deactivate`,
} as const;

export const EXPORT_ENDPOINTS = {
  sales: `${API_BASE_URL}/exports/sales`,
  profitLoss: `${API_BASE_URL}/exports/profit-loss`,
} as const;

export const CONNECTIVITY_ENDPOINTS = {
  ping: `${API_BASE_URL}/ping`,
} as const;

export const PURCHASE_ORDER_ENDPOINTS = {
  list: `${API_BASE_URL}/purchase-orders`,
  create: `${API_BASE_URL}/purchase-orders`,
  detail: (id: string) => `${API_BASE_URL}/purchase-orders/${id}`,
  place: (id: string) => `${API_BASE_URL}/purchase-orders/${id}/place`,
  delete: (id: string) => `${API_BASE_URL}/purchase-orders/${id}`,
  cancel: (id: string) => `${API_BASE_URL}/purchase-orders/${id}/cancel`,
  close: (id: string) => `${API_BASE_URL}/purchase-orders/${id}/close`,
  receipts: (id: string) => `${API_BASE_URL}/purchase-orders/${id}/receipts`,
} as const;

export const DASHBOARD_ENDPOINTS = {
  overview: `${API_BASE_URL}/dashboard`,
} as const;
