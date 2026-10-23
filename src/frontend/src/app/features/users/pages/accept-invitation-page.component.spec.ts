import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { Location } from '@angular/common';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { clearPendingInvitation, readPendingInvitation } from '../../../core/auth/pending-invitation';
import { UserAccountService } from '../services/user-account.service';
import { AcceptInvitationPageComponent } from './accept-invitation-page.component';

describe('AcceptInvitationPageComponent', () => {
  const acceptInvitation = vi.fn();
  const replaceState = vi.fn();
  const navigate = vi.fn();

  const refreshAccessToken = vi.fn();
  const authService = {
    session: signal<{ activeShopId: string | null } | null>({ activeShopId: 's1' }),
    refreshAccessToken,
  };

  function setup(fragment: string | null): AcceptInvitationPageComponent {
    TestBed.configureTestingModule({
      imports: [
        AcceptInvitationPageComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [
        { provide: ActivatedRoute, useValue: { snapshot: { fragment } } },
        { provide: Location, useValue: { replaceState } },
        { provide: Router, useValue: { navigate } },
        { provide: AuthService, useValue: authService },
        { provide: UserAccountService, useValue: { acceptInvitation } },
      ],
    });

    const fixture = TestBed.createComponent(AcceptInvitationPageComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    acceptInvitation.mockReset();
    replaceState.mockReset();
    navigate.mockReset();
    refreshAccessToken.mockReset();
    refreshAccessToken.mockReturnValue(of({ activeShopId: 's2' }));
    authService.session.set({ activeShopId: 's1' });
    clearPendingInvitation();
  });

  afterAll(() => clearPendingInvitation());

  afterEach(() => TestBed.resetTestingModule());

  it('acceptInviteShowsWrongAccountWithoutLeakingDestination', () => {
    acceptInvitation.mockReturnValue(
      throwError(() => ({ error: { code: 'Users.InvitationWrongAccount' } })),
    );

    const component = setup('invitationId=i1&token=SECRET&purpose=shop-invitation');
    component.onAccept();

    expect(component.error()).toBe('users.invitationWrongAccount');
    // Nothing about the invitation's destination is revealed: no membership, no shop, no role.
    expect(component.accepted()).toBeNull();
  });

  it('strips the single-use token out of the address bar before anything else', () => {
    setup('invitationId=i1&token=SECRET&purpose=shop-invitation');

    expect(replaceState).toHaveBeenCalledWith('/invitations/accept');
  });

  it('reports an incomplete link rather than calling the API', () => {
    const component = setup('invitationId=i1');

    expect(component.error()).toBe('users.invitationLinkInvalid');
    expect(component.ready()).toBe(false);

    component.onAccept();
    expect(acceptInvitation).not.toHaveBeenCalled();
  });

  it('refuses a link carrying two tokens', () => {
    const component = setup('invitationId=i1&token=A&token=B');

    expect(component.error()).toBe('users.invitationLinkInvalid');
    expect(component.ready()).toBe(false);
  });

  it('shows the joined role once acceptance succeeds and spends the link', () => {
    const membership = {
      userId: 'u9',
      shopId: 's2',
      displayName: 'Invited Person',
      role: 'Manager',
      status: 'Active',
      version: 0,
    };
    acceptInvitation.mockReturnValue(of(membership));

    const component = setup('invitationId=i1&token=SECRET');
    component.onAccept();

    expect(acceptInvitation).toHaveBeenCalledWith('i1', 'SECRET');
    expect(component.accepted()).toEqual(membership);
    expect(component.error()).toBe('');

    // Single use: a second press cannot replay the token.
    component.onAccept();
    expect(acceptInvitation).toHaveBeenCalledTimes(1);
  });

  it('maps a withdrawn or expired invitation to its own message', () => {
    acceptInvitation.mockReturnValue(
      throwError(() => ({ error: { code: 'Users.InvitationNotAcceptable' } })),
    );

    const component = setup('invitationId=i1&token=SECRET');
    component.onAccept();

    expect(component.error()).toBe('users.invitationNotAcceptable');
  });

  it('keeps the link usable when it is opened before signing in', () => {
    authService.session.set(null);

    const component = setup('invitationId=i1&token=SECRET&purpose=shop-invitation');

    // The address bar is still cleaned, but the link itself survives the sign-in detour instead
    // of forcing a second trip to the invitation email.
    expect(replaceState).toHaveBeenCalledWith('/invitations/accept');
    expect(component.error()).toBe('');
    expect(readPendingInvitation()).toEqual({ invitationId: 'i1', token: 'SECRET' });
  });

  it('picks the held link up again after the person signs in', () => {
    authService.session.set(null);
    setup('invitationId=i1&token=SECRET');
    TestBed.resetTestingModule();

    authService.session.set({ activeShopId: 's1' });
    acceptInvitation.mockReturnValue(
      of({ userId: 'u9', shopId: 's2', displayName: 'Invited', role: 'Manager', status: 'Active', version: 0 }),
    );

    // Back on the page with no fragment at all, because sign-in navigated away and back.
    const component = setup(null);
    expect(component.ready()).toBe(true);
    expect(component.error()).toBe('');

    component.onAccept();
    expect(acceptInvitation).toHaveBeenCalledWith('i1', 'SECRET');
    expect(readPendingInvitation()).toBeNull();
  });

  it('renews the session on success so the new shop is usable at once', () => {
    acceptInvitation.mockReturnValue(
      of({ userId: 'u9', shopId: 's2', displayName: 'Invited', role: 'Manager', status: 'Active', version: 0 }),
    );

    const component = setup('invitationId=i1&token=SECRET');
    component.onAccept();

    // Without this the new member's token still carries no membership in the shop they just
    // joined, and every shop-scoped call fails until it expires.
    expect(refreshAccessToken).toHaveBeenCalledTimes(1);
    expect(component.accepted()).not.toBeNull();
  });

  it('still reports success when the session renewal itself fails', () => {
    acceptInvitation.mockReturnValue(
      of({ userId: 'u9', shopId: 's2', displayName: 'Invited', role: 'Manager', status: 'Active', version: 0 }),
    );
    refreshAccessToken.mockReturnValue(throwError(() => new Error('offline')));

    const component = setup('invitationId=i1&token=SECRET');
    component.onAccept();

    expect(component.accepted()).not.toBeNull();
    expect(component.error()).toBe('');
  });
});
