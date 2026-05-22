import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, inject, signal } from '@angular/core';
import { CONNECTIVITY_ENDPOINTS } from '../auth/auth.constants';

const PING_TIMEOUT_MS = 5_000;
const PING_MAX_RETRIES = 2;
const PING_RETRY_DELAYS_MS = [500, 1_000] as const;
const SERVICE_WORKER_BYPASS_QUERY = 'ngsw-bypass=true';

type PingResponse = {
  serverTime: string;
};

function delay(ms: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms));
}

@Injectable({ providedIn: 'root' })
export class NetworkStatusService {
  private readonly platformId = inject(PLATFORM_ID);

  readonly isOnline = signal<boolean>(this.isBrowser() ? navigator.onLine : true);
  readonly canReachApi = signal<boolean>(false);
  readonly lastVerifiedAt = signal<Date | null>(null);
  readonly isChecking = signal<boolean>(false);

  constructor() {
    if (!this.isBrowser()) {
      return;
    }

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
    if (!this.isBrowser()) {
      return;
    }

    if (!navigator.onLine) {
      this.isOnline.set(false);
      this.canReachApi.set(false);
      return;
    }

    if (this.isChecking()) return;
    this.isChecking.set(true);

    try {
      for (let attempt = 0; attempt <= PING_MAX_RETRIES; attempt++) {
        if (!navigator.onLine) {
          this.isOnline.set(false);
          this.canReachApi.set(false);
          return;
        }

        if (attempt > 0) {
          await delay(PING_RETRY_DELAYS_MS[attempt - 1]);
          if (!navigator.onLine) {
            this.isOnline.set(false);
            this.canReachApi.set(false);
            return;
          }
        }

        const serverTime = await this.attemptPing();
        if (!navigator.onLine) {
          this.isOnline.set(false);
          this.canReachApi.set(false);
          return;
        }

        if (serverTime) {
          this.canReachApi.set(true);
          this.lastVerifiedAt.set(serverTime);
          return;
        }
      }
      this.canReachApi.set(false);
    } finally {
      this.isChecking.set(false);
    }
  }

  private async attemptPing(): Promise<Date | null> {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), PING_TIMEOUT_MS);

    try {
      const response = await fetch(this.withServiceWorkerBypass(CONNECTIVITY_ENDPOINTS.ping), {
        method: 'GET',
        cache: 'no-store',
        signal: controller.signal,
      });
      if (!response.ok) {
        return null;
      }

      const payload = await response.json() as PingResponse;
      const serverTime = new Date(payload.serverTime);

      return Number.isNaN(serverTime.getTime()) ? null : serverTime;
    } catch {
      return null;
    } finally {
      clearTimeout(timeout);
    }
  }

  private isBrowser(): boolean {
    return isPlatformBrowser(this.platformId);
  }

  private withServiceWorkerBypass(url: string): string {
    const separator = url.includes('?') ? '&' : '?';
    return `${url}${separator}${SERVICE_WORKER_BYPASS_QUERY}`;
  }
}
