import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { map, Observable, tap } from 'rxjs';

import { SHOP_ENDPOINTS } from '../../../core/auth/auth.constants';
import { AuthResult, UserShop } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';

export interface CreateShopRequest {
  readonly name: string;
  readonly address: string;
  readonly city: string;
  readonly state: string;
  readonly pincode: string;
  readonly contactPerson?: string;
  readonly mobileNumber?: string;
}

export interface ShopDetails {
  readonly shopId: string;
  readonly name: string;
  readonly address: string;
  readonly city: string;
  readonly state: string;
  readonly pincode: string;
  readonly contactPerson: string | null;
  readonly mobileNumber: string | null;
}

interface SetDefaultShopRequest {
  readonly shopId: string;
}

@Injectable({ providedIn: 'root' })
export class ShopService {
  private readonly http = inject(HttpClient);
  private readonly authService = inject(AuthService);

  getMyShops(): Observable<readonly UserShop[]> {
    return this.http.get<readonly UserShop[]>(SHOP_ENDPOINTS.me);
  }

  getShopDetails(shopId: string): Observable<ShopDetails> {
    return this.http.get<ShopDetails>(SHOP_ENDPOINTS.details(shopId));
  }

  createShop(payload: CreateShopRequest): Observable<void> {

    return this.http.post<AuthResult>(SHOP_ENDPOINTS.create, payload).pipe(
      tap((result) => this.authService.applyAuthResult(result)),
      map(() => void 0)
    );
  }

  setDefaultShop(shopId: string): Observable<void> {
    const payload: SetDefaultShopRequest = { shopId };

    return this.http.post<AuthResult>(SHOP_ENDPOINTS.setDefault, payload).pipe(
      tap((result) => this.authService.applyAuthResult(result)),
      map(() => void 0)
    );
  }

  updateShop(shopId: string, payload: CreateShopRequest): Observable<ShopDetails> {
    return this.http.put<ShopDetails>(SHOP_ENDPOINTS.update(shopId), payload);
  }
}
