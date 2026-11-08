import { HttpClient } from '@angular/common/http';
import { DestroyRef, Injectable, inject } from '@angular/core';
import { Observable, Subject, defer, firstValueFrom, map, tap } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { AuthService } from '../../../core/auth/auth.service';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { assertCurrentScope, scopedRequestContext } from '../../../core/context/scoped-request';
import { OfflineSalesSnapshotIndexedDbService } from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import type { ExpiryPolicyState, ExpiryPolicyUpdate } from './expiry.models';
export interface ExpiryReadiness {
  readonly businessDate: string;
  readonly catalogVersion: number;
  readonly results: {
    readonly totalLots: number;
    readonly missingDateLots: number;
    readonly expiredLots: number;
    readonly quarantinedLots: number;
    readonly physicalQuantity: number;
    readonly quarantinedQuantity: number;
    readonly items: readonly {
      batchId: string;
      itemId: string;
      itemName: string;
      batchNumber: string;
      expiryDate: string | null;
      physicalQuantity: number;
      quarantinedQuantity: number;
      missingDate: boolean;
      expired: boolean;
    }[];
  };
}
@Injectable({ providedIn: 'root' })
export class ExpiryPolicyService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly context = inject(ShopContextService);
  private readonly snapshots = inject(OfflineSalesSnapshotIndexedDbService);
  private readonly versions = new Map<string, number>();
  private readonly channel =
    typeof BroadcastChannel === 'undefined'
      ? null
      : new BroadcastChannel('intelibill-expiry-catalog');
  constructor() {
    if (this.channel)
      this.channel.onmessage = (event) => {
        if (typeof event.data === 'string') void this.invalidate(event.data, false).catch(() => {});
      };
    const interval = setInterval(() => {
      if (this.activeShopId()) this.read().subscribe({ next: () => {}, error: () => {} });
    }, 60000);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(interval);
      this.channel?.close();
    });
  }
  async invalidate(shopId: string, broadcast = true): Promise<void> {
    try {
      await this.snapshots.invalidateForShop(shopId);
    } finally {
      this.changes.next(shopId);
      if (broadcast) this.channel?.postMessage(shopId);
    }
  }
  private readonly changes = new Subject<string>();
  readonly changed$ = this.changes.asObservable();
  private readonly base = `${API_BASE_URL}/expiry-policies`;
  scopeIdentity(): string {
    const scope = this.context.scope();
    return scope ? `${scope.userId}:${scope.shopId}:${scope.epoch}` : '';
  }
  activeShopId(): string | null {
    return this.auth.session()?.activeShopId ?? null;
  }
  role(): string {
    const s = this.auth.session();
    return s?.shops?.find((x) => x.shopId === s.activeShopId)?.role.toLowerCase() ?? '';
  }
  read(itemId?: string): Observable<ExpiryPolicyState> {
    const shopId = this.activeShopId();
    return this.http.get<ExpiryPolicyState>(this.url(itemId)).pipe(
      tap((state) => {
        if (!shopId || shopId !== this.activeShopId()) return;
        const prior = this.versions.get(shopId);
        this.versions.set(shopId, state.catalogVersion);
        void this.snapshots
          .getUsableSnapshotInfo(shopId)
          .then((snapshot) => {
            if (
              (prior !== undefined && prior !== state.catalogVersion) ||
              (snapshot?.catalogVersion && snapshot.catalogVersion !== state.catalogVersion)
            )
              void this.invalidate(shopId).catch(() => {});
          })
          .catch(() => {});
      }),
    );
  }
  readiness(page = 1): Observable<ExpiryReadiness> {
    return this.http.get<ExpiryReadiness>(`${this.base}/readiness`, {
      params: { page, pageSize: 50 },
    });
  }
  businessTimeZone(): Observable<string> {
    const shopId = this.activeShopId();
    return this.http
      .get<{
        businessTimeZoneId: string;
      }>(`${API_BASE_URL}/shops/${encodeURIComponent(shopId ?? '')}`)
      .pipe(map((x) => x.businessTimeZoneId));
  }
  update(request: ExpiryPolicyUpdate, itemId?: string): Observable<ExpiryPolicyState> {
    const invocation = this.context.scope();
    const owner = invocation && { userId: invocation.userId, shopId: invocation.shopId };
    const frozen = structuredClone(request);
    return defer(async () => {
      if (!owner || this.role() !== 'owner') throw new Error('expiry.ownerOnly');
      assertCurrentScope(this.context, owner, invocation);
      if (
        !frozen.expectedCatalogVersion ||
        !frozen.reason.trim() ||
        frozen.reason.trim().length > 500
      )
        throw new Error('expiry.reasonRequired');
      let result: ExpiryPolicyState;
      try {
        result = await firstValueFrom(
          this.http.put<ExpiryPolicyState>(this.url(itemId), frozen, {
            context: scopedRequestContext(owner),
          }),
        );
      } finally {
        await this.invalidate(owner.shopId);
      }
      assertCurrentScope(this.context, owner, invocation);
      this.versions.set(owner.shopId, result.catalogVersion);
      return result;
    });
  }
  private url(itemId?: string): string {
    return itemId ? `${this.base}/items/${encodeURIComponent(itemId)}` : this.base;
  }
}
