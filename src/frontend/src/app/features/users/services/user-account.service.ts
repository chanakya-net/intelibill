import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { map, Observable, tap } from 'rxjs';

import { AuthUser } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { MEMBERSHIP_ENDPOINTS, USER_ENDPOINTS } from '../../../core/auth/auth.constants';

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

export type MembershipRole = 'Manager' | 'Staff';
export type MembershipStatus = 'Active' | 'Suspended' | 'Removed';

/**
 * One member of the active shop. `status` and `membershipVersion` are shop-scoped; there is no
 * global login flag here, because shop administration no longer reaches the account.
 */
export interface ShopUser {
  readonly userId: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly email: string | null;
  readonly phoneNumber: string | null;
  readonly role: string;
  readonly status: string;
  readonly membershipVersion: number;
  readonly shopIds: readonly string[];
}

/** The result of a membership change or an accepted invitation. */
export interface Membership {
  readonly userId: string;
  readonly shopId: string;
  readonly displayName: string;
  readonly role: string;
  readonly status: string;
  readonly version: number;
}

/**
 * `expectedVersion` is the version the screen was showing. The server refuses the change if the
 * membership moved on in the meantime, so two administrators cannot silently overwrite each other.
 */
export interface UpdateMembershipRequest {
  readonly role: MembershipRole;
  readonly status: MembershipStatus;
  readonly expectedVersion: number;
}

export interface InviteMemberRequest {
  readonly email: string;
  readonly role: MembershipRole;
}

export interface ShopInvitation {
  readonly invitationId: string;
  readonly shopId: string;
  readonly email: string;
  readonly role: string;
  readonly status: string;
  readonly expiresAt: string;
  readonly createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class UserAccountService {
  private readonly http = inject(HttpClient);
  private readonly authService = inject(AuthService);

  updateMyProfile(payload: UpdateMyProfileRequest): Observable<void> {
    // The saved profile comes back on its own. Editing a name issues no token, so there is nothing
    // for the client to adopt beyond the profile itself.
    return this.http.put<AuthUser>(USER_ENDPOINTS.me, payload).pipe(
      tap((user) => this.authService.applyUser(user)),
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

  updateMembership(
    shopId: string,
    userId: string,
    payload: UpdateMembershipRequest
  ): Observable<Membership> {
    return this.http.patch<Membership>(MEMBERSHIP_ENDPOINTS.update(shopId, userId), payload);
  }

  inviteMember(shopId: string, payload: InviteMemberRequest): Observable<ShopInvitation> {
    return this.http.post<ShopInvitation>(MEMBERSHIP_ENDPOINTS.invitations(shopId), payload);
  }

  getInvitations(shopId: string): Observable<readonly ShopInvitation[]> {
    return this.http.get<readonly ShopInvitation[]>(MEMBERSHIP_ENDPOINTS.invitations(shopId));
  }

  revokeInvitation(shopId: string, invitationId: string): Observable<void> {
    return this.http
      .delete(MEMBERSHIP_ENDPOINTS.invitation(shopId, invitationId))
      .pipe(map(() => void 0));
  }

  /**
   * Redeemed by the invited person from their own session. The token travels only in the invitation
   * email, so it is sent in the body rather than the URL.
   */
  acceptInvitation(invitationId: string, token: string): Observable<Membership> {
    return this.http.post<Membership>(MEMBERSHIP_ENDPOINTS.acceptInvitation(invitationId), {
      token,
    });
  }
}
