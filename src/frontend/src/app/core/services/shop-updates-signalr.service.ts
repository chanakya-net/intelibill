import { Injectable, PLATFORM_ID, effect, inject } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import * as signalR from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';
import { filter } from 'rxjs/operators';

import { AuthService } from '../auth/auth.service';
import { HUB_BASE_URL } from '../auth/auth.constants';

export interface ShopUpdatePayload {
  readonly eventType: string;
  readonly shopId: string;
  readonly changedIds: readonly string[];
  readonly occurredOn: string;
}

@Injectable({ providedIn: 'root' })
export class ShopUpdatesSignalRService {
  private readonly authService = inject(AuthService);
  private readonly platformId = inject(PLATFORM_ID);

  private connection: signalR.HubConnection | null = null;
  private connectionKey = '';
  private connectionSyncPromise: Promise<void> | null = null;
  private connectionSyncRequested = false;
  private readonly updateSubject = new Subject<ShopUpdatePayload>();

  readonly updates$ = this.updateSubject.asObservable().pipe(
    filter(() => {
      const session = this.authService.session();
      return !!session?.activeShopId;
    }),
  );

  constructor() {
    effect(() => {
      this.authService.session();
      void this.syncConnection();
    });
  }

  async startConnection(): Promise<void> {
    await this.syncConnection();
  }

  async stopConnection(): Promise<void> {
    this.connectionSyncRequested = false;
    if (!this.connection) {
      return;
    }
    try {
      await this.connection.stop();
    } catch (err) {
      console.error('[ShopUpdatesSignalRService] Stop failed:', err);
    } finally {
      this.connection = null;
    }
  }

  private async syncConnection(): Promise<void> {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    this.connectionSyncRequested = true;
    if (this.connectionSyncPromise) {
      return this.connectionSyncPromise;
    }

    this.connectionSyncPromise = this.flushConnectionSync();
    try {
      await this.connectionSyncPromise;
    } finally {
      this.connectionSyncPromise = null;
    }
  }

  private async flushConnectionSync(): Promise<void> {
    try {
      while (this.connectionSyncRequested) {
        this.connectionSyncRequested = false;
        await this.applyConnectionState();
      }
    } catch (err) {
      console.error('[ShopUpdatesSignalRService] Sync failed:', err);
    }
  }

  private async applyConnectionState(): Promise<void> {
    const session = this.authService.session();
    const activeShopId = session?.activeShopId ?? '';
    const accessToken = session?.accessToken ?? '';
    const desiredKey = activeShopId && accessToken ? `${activeShopId}::${accessToken}` : '';

    if (!desiredKey) {
      this.connectionKey = '';
      await this.disposeConnection();
      return;
    }

    if (this.connection && this.connectionKey === desiredKey) {
      return;
    }

    await this.disposeConnection();
    this.connectionKey = desiredKey;
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${HUB_BASE_URL}/hubs/shop-updates`, {
        accessTokenFactory: () => this.authService.getAccessToken() ?? '',
      })
      .withAutomaticReconnect()
      .build();

    this.connection.on('ShopUpdated', (payload: ShopUpdatePayload) => {
      const activeShopId = this.authService.session()?.activeShopId;
      if (!activeShopId || payload.shopId !== activeShopId) {
        return;
      }

      this.updateSubject.next(payload);
    });

    try {
      await this.connection.start();
    } catch (err) {
      console.error('[ShopUpdatesSignalRService] Connection failed:', err);
      await this.disposeConnection();
    }
  }

  private async disposeConnection(): Promise<void> {
    if (!this.connection) {
      return;
    }

    try {
      await this.connection.stop();
    } catch (err) {
      console.error('[ShopUpdatesSignalRService] Stop failed:', err);
    } finally {
      this.connection = null;
    }
  }
}
