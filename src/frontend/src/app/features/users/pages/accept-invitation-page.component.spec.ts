import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { Location } from '@angular/common';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { UserAccountService } from '../services/user-account.service';
import { AcceptInvitationPageComponent } from './accept-invitation-page.component';

describe('AcceptInvitationPageComponent', () => {
  const acceptInvitation = vi.fn();
  const replaceState = vi.fn();
  const navigate = vi.fn();

  const authService = {
    session: signal<{ activeShopId: string | null } | null>({ activeShopId: 's1' }),
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
  });

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
});
