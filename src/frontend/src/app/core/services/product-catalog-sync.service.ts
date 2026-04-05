import { Injectable, effect, inject, signal } from '@angular/core';

import { AuthService } from '../auth/auth.service';
import { ITEM_ENDPOINTS } from '../auth/auth.constants';
import {
  ProductCatalogEntry,
  ProductCatalogIndexedDbService,
} from '../storage/product-catalog-indexeddb.service';

const CHUNK_SIZE = 50;

@Injectable({ providedIn: 'root' })
export class ProductCatalogSyncService {
  private readonly authService = inject(AuthService);
  private readonly catalogDb = inject(ProductCatalogIndexedDbService);

  readonly catalogEntries = signal<readonly ProductCatalogEntry[]>([]);

  constructor() {
    effect(() => {
      const shopId = this.authService.session()?.activeShopId ?? null;
      if (!shopId) {
        this.catalogEntries.set([]);
        return;
      }
      void this.syncForShop(shopId);
    });
  }

  filterByName(query: string): ProductCatalogEntry[] {
    if (!query) return [];
    const q = query.toLowerCase();
    return this.catalogEntries().filter((e) => e.name.toLowerCase().includes(q));
  }

  filterByBarcode(query: string): ProductCatalogEntry[] {
    if (!query) return [];
    const q = query.toLowerCase();
    return this.catalogEntries().filter((e) => e.barcode.toLowerCase().startsWith(q));
  }

  findByName(name: string): ProductCatalogEntry | undefined {
    return this.catalogEntries().find((e) => e.name === name);
  }

  findByBarcode(barcode: string): ProductCatalogEntry | undefined {
    return this.catalogEntries().find((e) => e.barcode === barcode);
  }

  private async syncForShop(shopId: string): Promise<void> {
    try {
      // Load stale cache first so autocomplete works instantly while stream loads
      const cached = await this.catalogDb.getCatalog(shopId);
      if (cached.length > 0) {
        this.catalogEntries.set(cached);
      }

      const token = this.authService.getAccessToken();
      if (!token) return;

      const response = await fetch(ITEM_ENDPOINTS.stream, {
        headers: { Authorization: `Bearer ${token}` },
      });

      if (!response.ok || !response.body) return;

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';
      const accumulated: ProductCatalogEntry[] = [];

      while (true) {
        const { done, value } = await reader.read();

        if (done) {
          buffer += decoder.decode(); // flush remaining multi-byte sequences
          break;
        }

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop() ?? '';

        for (const line of lines) {
          const trimmed = line.trim();
          if (!trimmed) continue;
          try {
            const entry = JSON.parse(trimmed) as ProductCatalogEntry;
            accumulated.push(entry);

            // Progressive update every CHUNK_SIZE items
            if (accumulated.length % CHUNK_SIZE === 0) {
              this.catalogEntries.set([...accumulated]);
              void this.catalogDb.saveCatalog(shopId, [...accumulated]);
            }
          } catch {
            // skip malformed lines
          }
        }
      }

      // Process any remaining buffered content after stream ends
      const remaining = buffer.trim();
      if (remaining) {
        try {
          accumulated.push(JSON.parse(remaining) as ProductCatalogEntry);
        } catch {
          // skip
        }
      }

      // Final flush: update signal and persist the complete catalog
      if (accumulated.length > 0) {
        this.catalogEntries.set([...accumulated]);
        await this.catalogDb.saveCatalog(shopId, accumulated);
      }
    } catch {
      // Non-blocking — errors are silently swallowed
    }
  }
}
