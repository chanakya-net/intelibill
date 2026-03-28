export const API_BASE_URL = 'http://localhost:5202/api';

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
  me: `${API_BASE_URL}/users/me`,
  changePassword: `${API_BASE_URL}/users/me/change-password`,
} as const;
