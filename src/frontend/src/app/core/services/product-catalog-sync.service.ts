import { Injectable, effect, inject, signal } from '@angular/core';

import { AuthService } from '../auth/auth.service';
import { ITEM_ENDPOINTS } from '../auth/auth.constants';
import { ShopContextService } from '../context/shop-context.service';
import {
  ProductCatalogEntry,
  ProductCatalogIndexedDbService,
} from '../storage/product-catalog-indexeddb.service';

const CHUNK_SIZE = 50;

@Injectable({ providedIn: 'root' })
export class ProductCatalogSyncService {
  private readonly authService = inject(AuthService);
  private readonly catalogDb = inject(ProductCatalogIndexedDbService);
  private readonly shopContext = inject(ShopContextService);

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

  upsertEntry(entry: ProductCatalogEntry): void {
    const current = this.catalogEntries();
    const idx = current.findIndex((e) => e.barcode === entry.barcode);
    const updated =
      idx >= 0
        ? [...current.slice(0, idx), entry, ...current.slice(idx + 1)]
        : [...current, entry];
    this.catalogEntries.set(updated);

    const shopId = this.authService.session()?.activeShopId ?? null;
    if (shopId) {
      void this.catalogDb.saveCatalog(shopId, updated);
    }
  }

  /**
   * Streams the catalog of the shop the tab is in when the sync starts. Every await is followed by a
   * check that the tab is still in that scope: `fetch` is not seen by the HTTP interceptor, so a
   * stream that outlives a switch or sign-out stops itself instead of filing rows under another shop.
   */
  private async syncForShop(shopId: string): Promise<void> {
    const epoch = this.shopContext.epoch();
    const scopeMoved = (): boolean => this.shopContext.epoch() !== epoch;

    try {
      // Load stale cache first so autocomplete works instantly while stream loads
      const cached = await this.catalogDb.getCatalog(shopId);
      if (scopeMoved()) return;
      if (cached.length > 0) {
        this.catalogEntries.set(cached);
      }

      const token = this.authService.getAccessToken();
      if (!token) return;

      const response = await fetch(ITEM_ENDPOINTS.stream, {
        headers: { Authorization: `Bearer ${token}` },
      });

      if (scopeMoved()) {
        void response.body?.cancel();
        return;
      }
      if (!response.ok || !response.body) return;

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';
      const accumulated: ProductCatalogEntry[] = [];

      while (true) {
        const { done, value } = await reader.read();
        if (scopeMoved()) {
          void reader.cancel();
          return;
        }

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
