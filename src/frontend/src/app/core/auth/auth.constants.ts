export const API_BASE_URL = 'http://localhost:5202/api';

export const AUTH_ENDPOINTS = {
  registerWithEmail: `${API_BASE_URL}/auth/register/email`,
  loginWithEmail: `${API_BASE_URL}/auth/login/email`,
  refreshToken: `${API_BASE_URL}/auth/token/refresh`,
  revokeToken: `${API_BASE_URL}/auth/token/revoke`,
} as const;
