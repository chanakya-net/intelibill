import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { map, Observable, tap } from 'rxjs';

import { AuthResult } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { USER_ENDPOINTS } from '../../../core/auth/auth.constants';

export interface UpdateMyProfileRequest {
  readonly email: string;
  readonly phoneNumber: string | null;
  readonly firstName: string;
  readonly lastName: string;
  readonly language?: string;
}

export interface ChangeMyPasswordRequest {
  readonly currentPassword: string;
  readonly newPassword: string;
}

export interface ShopUser {
  readonly userId: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly email: string | null;
  readonly phoneNumber: string | null;
  readonly role: string;
  readonly isLoginEnabled: boolean;
  readonly shopIds: readonly string[];
}

export interface AddShopUserRequest {
  readonly shopIds: readonly string[];
  readonly email: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly phoneNumber: string;
  readonly password: string;
  readonly confirmPassword: string;
  readonly role: 'Manager' | 'Staff';
}

export interface EditShopUserRequest {
  readonly email: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly phoneNumber: string;
  readonly role: 'Manager' | 'Staff';
  readonly isLoginEnabled: boolean;
  readonly shopIds?: readonly string[];
}

@Injectable({ providedIn: 'root' })
export class UserAccountService {
  private readonly http = inject(HttpClient);
  private readonly authService = inject(AuthService);

  updateMyProfile(payload: UpdateMyProfileRequest): Observable<void> {
    return this.http.put<AuthResult>(USER_ENDPOINTS.me, payload).pipe(
      tap((result) => this.authService.applyAuthResult(result)),
      map(() => void 0)
    );
  }

  changeMyPassword(payload: ChangeMyPasswordRequest): Observable<void> {
    return this.http.post(USER_ENDPOINTS.changePassword, payload).pipe(
      map(() => void 0)
    );
  }

  getShopUsers(): Observable<readonly ShopUser[]> {
    return this.http.get<readonly ShopUser[]>(USER_ENDPOINTS.list);
  }

  addShopUser(payload: AddShopUserRequest): Observable<ShopUser> {
    return this.http.post<ShopUser>(USER_ENDPOINTS.add, payload);
  }

  editShopUser(userId: string, payload: EditShopUserRequest): Observable<ShopUser> {
    return this.http.put<ShopUser>(USER_ENDPOINTS.update(userId), payload);
  }
}
