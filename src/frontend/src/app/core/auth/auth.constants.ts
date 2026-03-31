import { environment } from '../../../environments/environment';

export const API_BASE_URL = environment.apiBaseUrl;

export const AUTH_ENDPOINTS = {
  registerWithEmail: `${API_BASE_URL}/auth/register/email`,
  loginWithEmail: `${API_BASE_URL}/auth/login/email`,
  refreshToken: `${API_BASE_URL}/auth/token/refresh`,
  revokeToken: `${API_BASE_URL}/auth/token/revoke`,
} as const;

export const SHOP_ENDPOINTS = {
  me: `${API_BASE_URL}/shops/me`,
  create: `${API_BASE_URL}/shops`,
  setDefault: `${API_BASE_URL}/shops/default`,
  details: (shopId: string) => `${API_BASE_URL}/shops/${shopId}`,
  update: (shopId: string) => `${API_BASE_URL}/shops/${shopId}`,
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
} as const;
