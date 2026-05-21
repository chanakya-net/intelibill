import { Injectable, signal } from '@angular/core';
import { CONNECTIVITY_ENDPOINTS } from '../auth/auth.constants';

const PING_TIMEOUT_MS = 5_000;

@Injectable({ providedIn: 'root' })
export class NetworkStatusService {
  readonly isOnline = signal<boolean>(navigator.onLine);
  readonly canReachApi = signal<boolean>(false);
  readonly lastVerifiedAt = signal<Date | null>(null);
  readonly isChecking = signal<boolean>(false);

  constructor() {
    window.addEventListener('online', () => {
      this.isOnline.set(true);
      void this.checkConnectivity();
    });

    window.addEventListener('offline', () => {
      this.isOnline.set(false);
      this.canReachApi.set(false);
    });
  }

  async checkConnectivity(): Promise<void> {
    if (this.isChecking()) return;
    this.isChecking.set(true);

    try {
      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), PING_TIMEOUT_MS);

      try {
        const response = await fetch(CONNECTIVITY_ENDPOINTS.ping, {
          method: 'GET',
          cache: 'no-store',
          signal: controller.signal,
        });

        if (response.ok) {
          this.canReachApi.set(true);
          this.lastVerifiedAt.set(new Date());
        } else {
          this.canReachApi.set(false);
        }
      } finally {
        clearTimeout(timeout);
      }
    } catch {
      this.canReachApi.set(false);
    } finally {
      this.isChecking.set(false);
    }
  }
}
