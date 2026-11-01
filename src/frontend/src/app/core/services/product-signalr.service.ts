import { Injectable, inject } from '@angular/core';

import { ProductCatalogSyncService } from './product-catalog-sync.service';
import { ProductCatalogEntry } from '../storage/product-catalog-indexeddb.service';
import { RealtimeHubConnection } from './realtime-hub-connection';

interface ProductAddedPayload {
  readonly itemId: string;
  readonly barcode: string;
  readonly name: string;
  readonly shopId: string;
}

@Injectable({ providedIn: 'root' })
export class ProductSignalRService extends RealtimeHubConnection<ProductAddedPayload> {
  private readonly catalogSync = inject(ProductCatalogSyncService);

  protected readonly hubPath = '/hubs/products';
  protected readonly eventName = 'ProductAdded';
  protected readonly logPrefix = '[ProductSignalRService]';

  protected override handle(payload: ProductAddedPayload): void {
    const entry: ProductCatalogEntry = {
      itemId: payload.itemId,
      name: payload.name,
      barcode: payload.barcode,
    };
    this.catalogSync.upsertEntry(entry);
  }
}
