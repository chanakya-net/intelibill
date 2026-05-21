import { Injectable, signal } from '@angular/core';
import { CONNECTIVITY_ENDPOINTS } from '../auth/auth.constants';

const PING_TIMEOUT_MS = 5_000;
const PING_MAX_RETRIES = 2;
const PING_RETRY_DELAYS_MS = [500, 1_000] as const;

function delay(ms: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms));
}

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
      for (let attempt = 0; attempt <= PING_MAX_RETRIES; attempt++) {
        if (attempt > 0) {
          await delay(PING_RETRY_DELAYS_MS[attempt - 1]);
        }

        const success = await this.attemptPing();
        if (success) {
          this.canReachApi.set(true);
          this.lastVerifiedAt.set(new Date());
          return;
        }
      }
      this.canReachApi.set(false);
    } finally {
      this.isChecking.set(false);
    }
  }

  private async attemptPing(): Promise<boolean> {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), PING_TIMEOUT_MS);

    try {
      const response = await fetch(CONNECTIVITY_ENDPOINTS.ping, {
        method: 'GET',
        cache: 'no-store',
        signal: controller.signal,
      });
      return response.ok;
    } catch {
      return false;
    } finally {
      clearTimeout(timeout);
    }
  }
}
