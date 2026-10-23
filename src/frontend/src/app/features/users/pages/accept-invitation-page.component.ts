import { Location } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { catchError, finalize, map, of, switchMap } from 'rxjs';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import {
  clearPendingInvitation,
  PendingInvitation,
  readPendingInvitation,
  storePendingInvitation,
} from '../../../core/auth/pending-invitation';
import { Membership, UserAccountService } from '../services/user-account.service';

/**
 * Redeems a shop invitation from the invited person's own session.
 *
 * The page shows the invitation's destination only once acceptance has succeeded. A link opened by
 * the wrong account is told that it belongs to a different address and nothing else: it does not
 * learn which shop was inviting, which role was offered, or whether the link is still live.
 */
@Component({
  selector: 'app-accept-invitation-page',
  standalone: true,
  imports: [RouterLink, TranslocoPipe, ButtonModule],
  templateUrl: './accept-invitation-page.component.html',
  styleUrl: './accept-invitation-page.component.scss',
})
export class AcceptInvitationPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly router = inject(Router);
  private readonly users = inject(UserAccountService);
  readonly auth = inject(AuthService);

  private invitationId: string | null = null;
  private token: string | null = null;

  readonly busy = signal(false);
  readonly error = signal('');
  readonly accepted = signal<Membership | null>(null);

  /** True once a usable link is in hand; the token itself is never rendered. */
  readonly ready = signal(false);

  ngOnInit(): void {
    const fragment = this.route.snapshot.fragment;

    // The token must not survive in the address bar, browser history or a referrer header. It is
    // handed to this tab's session storage first, so that signing in — the normal state for
    // someone being onboarded — does not throw the invitation away and force a second trip to
    // the email.
    const link = this.readLink(fragment) ?? readPendingInvitation();
    this.location.replaceState('/invitations/accept');

    if (link === null) {
      this.error.set('users.invitationLinkInvalid');
      return;
    }

    this.invitationId = link.invitationId;
    this.token = link.token;
    storePendingInvitation(link);
    this.ready.set(true);
  }

  private readLink(fragment: string | null): PendingInvitation | null {
    if (!fragment) {
      return null;
    }

    const params = new URLSearchParams(fragment);
    const invitationId = params.get('invitationId');
    const token = params.get('token');

    if (
      params.getAll('invitationId').length !== 1 ||
      params.getAll('token').length !== 1 ||
      !invitationId ||
      !token
    ) {
      return null;
    }

    return { invitationId, token };
  }

  onAccept(): void {
    if (this.busy() || !this.invitationId || !this.token) {
      return;
    }

    this.error.set('');
    this.busy.set(true);

    this.users
      .acceptInvitation(this.invitationId, this.token)
      .pipe(
        // The new membership only reaches the app through a token, so renew the session before
        // leaving this page; otherwise the new member lands with no active shop until their
        // access token expires.
        switchMap((membership) =>
          this.auth.refreshAccessToken().pipe(
            catchError(() => of(null)),
            map(() => membership),
          ),
        ),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (membership) => {
          this.accepted.set(membership);
          // The invitation is single use, so the link in hand is spent either way.
          this.invitationId = null;
          this.token = null;
          this.ready.set(false);
          clearPendingInvitation();
        },
        error: (response: { error?: ApiErrorPayload }) =>
          this.error.set(acceptInvitationError(response.error)),
      });
  }

  onContinue(): void {
    void this.router.navigate(['/']);
  }
}

function acceptInvitationError(error: ApiErrorPayload | undefined): string {
  switch (error?.code ?? error?.title ?? '') {
    case 'Users.InvitationWrongAccount':
      return 'users.invitationWrongAccount';
    case 'Users.InvitationRequiresVerifiedEmail':
      return 'users.invitationRequiresVerifiedEmail';
    case 'Users.InvitationNotAcceptable':
      return 'users.invitationNotAcceptable';
    case 'Users.InvitationNotFound':
      return 'users.invitationNotFound';
    default:
      return 'users.invitationUnavailable';
  }
}
