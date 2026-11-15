import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { defer, finalize, map, Observable, shareReplay, takeUntil } from 'rxjs';

import { SHOP_ENDPOINTS } from '../../../core/auth/auth.constants';
import { assertCurrentScope, scopedRequestContext } from '../../../core/context/scoped-request';
import { ShopContextService } from '../../../core/context/shop-context.service';
import type { ShopScope } from '../../../core/context/shop-scope';
import type {
  SaveShopSetupPreferences,
  ShopSetupPreferences,
  ShopSetupSample,
  ShopSetupStatus,
} from './shop-setup.models';

@Injectable({ providedIn: 'root' })
export class ShopSetupService {
  private readonly http = inject(HttpClient);
  private readonly context = inject(ShopContextService);

  getStatus(scope: ShopScope): Observable<ShopSetupStatus> {
    return this.read<ShopSetupStatus>(scope, SHOP_ENDPOINTS.setup(scope.shopId));
  }

  getPreferences(scope: ShopScope): Observable<ShopSetupPreferences> {
    return this.read<ShopSetupPreferences>(scope, `${SHOP_ENDPOINTS.setup(scope.shopId)}/preferences`);
  }

  getSample(scope: ShopScope): Observable<ShopSetupSample> {
    return this.read<ShopSetupSample>(scope, `${SHOP_ENDPOINTS.setup(scope.shopId)}/sample`);
  }

  save(scope: ShopScope, preferences: SaveShopSetupPreferences): Observable<ShopSetupPreferences> {
    return defer(() => {
      this.assertScope(scope);
      const pending = this.context.beginMutation();
      return this.http
        .put<ShopSetupPreferences>(`${SHOP_ENDPOINTS.setup(scope.shopId)}/preferences`, preferences, {
          context: scopedRequestContext(scope),
          timeout: 15_000,
        })
        .pipe(
          map((result) => {
            this.assertScope(scope);
            return result;
          }),
          finalize(() => pending.release()),
        );
    }).pipe(shareReplay({ bufferSize: 1, refCount: false }));
  }

  private read<T>(scope: ShopScope, url: string): Observable<T> {
    return defer(() => {
      this.assertScope(scope);
      return this.http.get<T>(url, { context: scopedRequestContext(scope) }).pipe(
        map((result) => {
          this.assertScope(scope);
          return result;
        }),
        takeUntil(this.context.changes$),
      );
    });
  }

  private assertScope(scope: ShopScope): void {
    assertCurrentScope(this.context, scope, scope);
  }
}
