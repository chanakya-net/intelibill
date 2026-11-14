import { HttpErrorResponse } from '@angular/common/http';
import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { finalize } from 'rxjs';
import { AdminInvitationsApi } from './admin-invitations-api.service';

type View = 'loading' | 'accept' | 'pending' | 'closed';

const messages: Readonly<Record<string, string>> = {
  'Invitation.Invalid': 'Use the email address this invitation was sent to and a password of at least 12 characters.',
  'Invitation.Unavailable': 'This invitation has expired or was already used. Ask a provider owner for a new one.',
  'Admin.Throttled': 'Too many attempts. Try again later.',
  'Admin.RequestRejected': 'This request expired. Reload the page and try again.',
};

/** Reads the invitation token from the link's fragment, then removes it from the address bar. */
function takeInvitationToken(fragment: string | null): string | undefined {
  const token = new URLSearchParams(fragment ?? '').get('token');
  return token && /^i1_[A-Za-z0-9_-]{86}$/.test(token) ? token : undefined;
}

@Component({
  selector: 'adm-accept-invitation-page',
  imports: [ButtonDirective, RouterLink],
  templateUrl: './accept-invitation-page.component.html',
  styleUrl: '../form-page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AcceptInvitationPageComponent {
  private readonly api = inject(AdminInvitationsApi);
  private readonly destroy = inject(DestroyRef);
  private invitationToken?: string;
  private requestToken?: string;

  protected readonly view = signal<View>('loading');
  protected readonly error = signal('');
  protected readonly busy = signal(false);

  constructor() {
    this.invitationToken = takeInvitationToken(inject(ActivatedRoute).snapshot.fragment);
    inject(Location).replaceState('/auth/invitation');
    if (this.invitationToken) this.loadContext();
    else this.readEnrollment();
  }

  protected submit(event: SubmitEvent, email: HTMLInputElement, password: HTMLInputElement) {
    event.preventDefault();
    if (this.busy() || !this.requestToken || !this.invitationToken) return;
    if (new TextEncoder().encode(password.value).length > 72) {
      this.error.set('Use a password of at most 72 UTF-8 bytes.');
      return;
    }

    const request = this.api.accept(this.requestToken, this.invitationToken, email.value.trim(), password.value);
    password.value = '';
    this.busy.set(true);
    this.error.set('');
    request.pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false))).subscribe({
      next: () => {
        this.invitationToken = undefined;
        this.view.set('pending');
      },
      error: (error: HttpErrorResponse) => {
        const code = problemCode(error.error) ?? '';
        if (code === 'Invitation.Unavailable') {
          this.invitationToken = undefined;
          this.view.set('closed');
        }
        this.error.set(messages[code] ?? 'The invitation could not be accepted. Try again.');
      },
    });
  }

  private loadContext() {
    this.api.acceptanceContext().pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: (context) => {
        this.requestToken = context.requestToken;
        this.view.set(context.available && context.requestToken ? 'accept' : 'closed');
      },
      error: () => this.reportUnavailable(),
    });
  }

  private readEnrollment() {
    this.api.enrollment().pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: () => this.view.set('pending'),
      error: (error: HttpErrorResponse) => {
        if (error.status !== 401) this.reportUnavailable();
        else this.view.set('closed');
      },
    });
  }

  private reportUnavailable() {
    this.view.set('closed');
    this.error.set('Provider access is temporarily unavailable. Reload the page to try again.');
  }
}
