import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { defer, finalize, tap } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { AuthService } from '../../../core/auth/auth.service';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { ScopeMismatchError, scopedRequestContext } from '../../../core/context/scoped-request';
import { TillSettingsEvents } from '../../tills/services/till-settings-events.service';

export interface ShopTillSettings {
  shopId: string;
  available: boolean;
  enabled: boolean;
  version: number;
  openSessionCount: number;
  awaitingReviewCount: number;
  pendingOfflineCount: number;
}
export interface UpdateShopTillSettings {
  enabled: boolean;
  expectedVersion: number;
  reason: string;
  confirmed: boolean;
}
@Injectable({ providedIn: 'root' })
export class ShopTillSettingsService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly context = inject(ShopContextService);
  private readonly events = inject(TillSettingsEvents);
  private readonly versions = new Map<string, number>();
  activeShopId() {
    return this.auth.session()?.activeShopId ?? null;
  }
  role() {
    const session = this.auth.session();
    return session?.shops.find((s) => s.shopId === session.activeShopId)?.role.toLowerCase() ?? '';
  }
  scopeIdentity() {
    const scope = this.context.scope();
    return scope ? `${scope.userId}:${scope.shopId}:${scope.epoch}` : '';
  }
  read(shopId: string) {
    return defer(() =>
      this.http.get<ShopTillSettings>(this.url(shopId), {
        context: scopedRequestContext(this.owner(shopId)),
      }),
    ).pipe(
      tap((state) => {
        const previous = this.versions.get(shopId);
        this.versions.set(shopId, state.version);
        if (previous !== undefined && previous !== state.version) this.events.publish(shopId);
      }),
    );
  }
  update(shopId: string, request: UpdateShopTillSettings) {
    return defer(() => {
      const owner = this.owner(shopId);
      if (this.role() !== 'owner') throw new ScopeMismatchError();
      const mutation = this.context.beginMutation();
      return this.http
        .put<ShopTillSettings>(this.url(shopId), request, { context: scopedRequestContext(owner) })
        .pipe(
          tap((state) => {
            this.versions.set(shopId, state.version);
            this.events.publish(shopId);
          }),
          finalize(() => mutation.release()),
        );
    });
  }
  private owner(shopId: string) {
    const scope = this.context.scope();
    if (
      !scope ||
      scope.shopId !== shopId ||
      shopId !== this.activeShopId() ||
      scope.userId !== this.auth.session()?.user.id
    )
      throw new ScopeMismatchError();
    return { userId: scope.userId, shopId };
  }
  private url(shopId: string) {
    return `${API_BASE_URL}/shops/${encodeURIComponent(shopId)}/till-settings`;
  }
}
