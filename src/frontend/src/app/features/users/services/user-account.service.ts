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
}

export interface ChangeMyPasswordRequest {
  readonly currentPassword: string;
  readonly newPassword: string;
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
}
