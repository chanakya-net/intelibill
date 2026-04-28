import { Injectable, PLATFORM_ID, inject } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import * as signalR from '@microsoft/signalr';

import { AuthService } from '../auth/auth.service';
import { ProductCatalogSyncService } from './product-catalog-sync.service';
import { ProductCatalogEntry } from '../storage/product-catalog-indexeddb.service';
import { HUB_BASE_URL } from '../auth/auth.constants';

interface ProductAddedPayload {
  readonly itemId: string;
  readonly barcode: string;
  readonly name: string;
  readonly shopId: string;
}

@Injectable({ providedIn: 'root' })
export class ProductSignalRService {
  private readonly authService = inject(AuthService);
  private readonly catalogSync = inject(ProductCatalogSyncService);
  private readonly platformId = inject(PLATFORM_ID);

  private connection: signalR.HubConnection | null = null;

  async startConnection(): Promise<void> {
    if (!isPlatformBrowser(this.platformId) || this.connection) {
      return;
    }

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${HUB_BASE_URL}/hubs/products`, {
        accessTokenFactory: () => this.authService.getAccessToken() ?? '',
      })
      .withAutomaticReconnect()
      .build();

    this.connection.on('ProductAdded', (payload: ProductAddedPayload) => {
      const activeShopId = this.authService.session()?.activeShopId;
      if (!activeShopId || payload.shopId !== activeShopId) {
        return;
      }

      const entry: ProductCatalogEntry = {
        itemId: payload.itemId,
        name: payload.name,
        barcode: payload.barcode,
      };
      this.catalogSync.upsertEntry(entry);
    });

    try {
      await this.connection.start();
    } catch (err) {
      console.error('[ProductSignalRService] Connection failed:', err);
    }
  }

  async stopConnection(): Promise<void> {
    if (!this.connection) {
      return;
    }
    try {
      await this.connection.stop();
    } catch (err) {
      console.error('[ProductSignalRService] Stop failed:', err);
    } finally {
      this.connection = null;
    }
  }
}
