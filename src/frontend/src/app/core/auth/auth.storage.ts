import { Injectable } from '@angular/core';

import { AuthSession } from './auth.models';

const LOCAL_SESSION_KEY = 'inventory.auth.session.local';
const SESSION_SESSION_KEY = 'inventory.auth.session.temporary';
const LAST_EMAIL_KEY = 'inventory.auth.last-email';

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

  saveLastEmail(email: string): void {
    localStorage.setItem(LAST_EMAIL_KEY, email);
  }

  getLastEmail(): string {
    return localStorage.getItem(LAST_EMAIL_KEY) ?? '';
  }

  clearLastEmail(): void {
    localStorage.removeItem(LAST_EMAIL_KEY);
  }

  private readSessionFromStorage(storage: Storage, key: string): AuthSession | null {
    const raw = storage.getItem(key);
    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw) as AuthSession;
    } catch {
      storage.removeItem(key);
      return null;
    }
  }
}
