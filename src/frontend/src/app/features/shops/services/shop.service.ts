import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { map, Observable, tap } from 'rxjs';

import { SHOP_ENDPOINTS } from '../../../core/auth/auth.constants';
import { AuthResult, UserShop } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';

interface CreateShopRequest {
  readonly name: string;
}

@Injectable({ providedIn: 'root' })
export class ShopService {
  private readonly http = inject(HttpClient);
  private readonly authService = inject(AuthService);

  getMyShops(): Observable<readonly UserShop[]> {
    return this.http.get<readonly UserShop[]>(SHOP_ENDPOINTS.me);
  }

  createShop(name: string): Observable<void> {
    const payload: CreateShopRequest = { name };

    return this.http.post<AuthResult>(SHOP_ENDPOINTS.create, payload).pipe(
      tap((result) => this.authService.applyAuthResult(result)),
      map(() => void 0)
    );
  }
}
