import { Injectable } from '@angular/core';

import { AuthSession } from './auth.models';

const LOCAL_SESSION_KEY = 'inventory.auth.session.local';
const SESSION_SESSION_KEY = 'inventory.auth.session.temporary';
const LAST_IDENTIFIER_KEY = 'inventory.auth.last-identifier';
const LEGACY_LAST_EMAIL_KEY = 'inventory.auth.last-email';
const LANGUAGE_KEY = 'inventory.preferences.language';

@Injectable({ providedIn: 'root' })
export class AuthStorage {
  loadSession(): AuthSession | null {
    const session = this.readSessionFromStorage(localStorage, LOCAL_SESSION_KEY)
      ?? this.readSessionFromStorage(sessionStorage, SESSION_SESSION_KEY);

    return session;
  }

  saveSession(session: AuthSession): void {
    this.clearSession();

    if (session.rememberMe) {
      localStorage.setItem(LOCAL_SESSION_KEY, JSON.stringify(session));
      return;
    }

    sessionStorage.setItem(SESSION_SESSION_KEY, JSON.stringify(session));
  }

  clearSession(): void {
    localStorage.removeItem(LOCAL_SESSION_KEY);
    sessionStorage.removeItem(SESSION_SESSION_KEY);
  }

  saveLastIdentifier(identifier: string): void {
    localStorage.setItem(LAST_IDENTIFIER_KEY, identifier);
  }

  getLastIdentifier(): string {
    return localStorage.getItem(LAST_IDENTIFIER_KEY) ?? localStorage.getItem(LEGACY_LAST_EMAIL_KEY) ?? '';
  }

  clearLastIdentifier(): void {
    localStorage.removeItem(LAST_IDENTIFIER_KEY);
    localStorage.removeItem(LEGACY_LAST_EMAIL_KEY);
  }

  saveLanguage(language: string): void {
    localStorage.setItem(LANGUAGE_KEY, language);
  }

  getLanguage(): string {
    return localStorage.getItem(LANGUAGE_KEY) ?? 'en-IN';
  }

  clearLanguage(): void {
    localStorage.removeItem(LANGUAGE_KEY);
  }

  private readSessionFromStorage(storage: Storage, key: string): AuthSession | null {
    const raw = storage.getItem(key);
    if (!raw) {
      return null;
    }

    try {
      const parsed = JSON.parse(raw) as Partial<AuthSession>;

      if (!parsed.accessToken || !parsed.refreshToken || !parsed.accessTokenExpiresAt || !parsed.refreshTokenExpiresAt || !parsed.user) {
        storage.removeItem(key);
        return null;
      }

      return {
        accessToken: parsed.accessToken,
        refreshToken: parsed.refreshToken,
        accessTokenExpiresAt: parsed.accessTokenExpiresAt,
        refreshTokenExpiresAt: parsed.refreshTokenExpiresAt,
        rememberMe: parsed.rememberMe ?? key === LOCAL_SESSION_KEY,
        user: parsed.user,
        activeShopId: parsed.activeShopId ?? null,
        shops: parsed.shops ?? [],
      };
    } catch {
      storage.removeItem(key);
      return null;
    }
  }
}
