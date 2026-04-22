import { environment } from '../../../environments/environment';

export const API_BASE_URL = environment.apiBaseUrl;

export const AUTH_ENDPOINTS = {
  registerWithEmail: `${API_BASE_URL}/auth/register/email`,
  loginWithEmail: `${API_BASE_URL}/auth/login/email`,
  loginExternalInit: `${API_BASE_URL}/auth/login/external/init`,
  loginExternalCallback: `${API_BASE_URL}/auth/login/external/callback`,
  refreshToken: `${API_BASE_URL}/auth/token/refresh`,
  revokeToken: `${API_BASE_URL}/auth/token/revoke`,
} as const;

export const SHOP_ENDPOINTS = {
  me: `${API_BASE_URL}/shops/me`,
  create: `${API_BASE_URL}/shops`,
  setDefault: `${API_BASE_URL}/shops/default`,
  details: (shopId: string) => `${API_BASE_URL}/shops/${shopId}`,
  update: (shopId: string) => `${API_BASE_URL}/shops/${shopId}`,
  addBankAccount: (shopId: string) => `${API_BASE_URL}/shops/${shopId}/bank-accounts`,
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

export const SALE_ENDPOINTS = {
  list: `${API_BASE_URL}/sales`,
  record: `${API_BASE_URL}/sales`,
  detail: (saleId: string) => `${API_BASE_URL}/sales/${saleId}`,
} as const;

export const EXPENSE_ENDPOINTS = {
  list: `${API_BASE_URL}/expenses`,
  record: `${API_BASE_URL}/expenses`,
  detail: (expenseId: string) => `${API_BASE_URL}/expenses/${expenseId}`,
  correct: (expenseId: string) => `${API_BASE_URL}/expenses/${expenseId}/correct`,
  categories: `${API_BASE_URL}/expenses/categories`,
} as const;
