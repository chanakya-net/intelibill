import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { finalize } from 'rxjs';
import { AdminInvitationsApi } from './admin-invitations-api.service';

type View = 'loading' | 'form' | 'sent' | 'denied';

const messages: Readonly<Record<string, string>> = {
  'Invitation.InvalidEmail': "Enter the administrator's email address.",
  'Invitation.AlreadyAdmin': 'This email already belongs to an administrator.',
  'Invitation.DeliveryUnavailable': 'The invitation email could not be sent. Try again later.',
  'Admin.RecentMfaRequired': 'Verify your authenticator again to manage administrators.',
  'Admin.AccessDenied': 'You cannot manage administrator invitations.',
  'Admin.Throttled': 'Too many invitations. Try again later.',
  'Admin.RequestRejected': 'This request expired. Reload the page and try again.',
};

@Component({
  selector: 'adm-invite-admin-page',
  imports: [ButtonDirective, DatePipe, RouterLink],
  templateUrl: './invite-admin-page.component.html',
  styleUrl: '../form-page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InviteAdminPageComponent {
  private readonly api = inject(AdminInvitationsApi);
  private readonly destroy = inject(DestroyRef);
  private requestToken?: string;

  protected readonly view = signal<View>('loading');
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  protected readonly sent = signal<{ email: string; expiresAt: string } | null>(null);

  constructor() {
    this.api.ownerContext().pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: (context) => {
        this.requestToken = context.requestToken;
        this.view.set(context.requestToken ? 'form' : 'denied');
      },
      error: (error: HttpErrorResponse) => this.deny(error),
    });
  }

  protected submit(event: SubmitEvent, email: HTMLInputElement) {
    event.preventDefault();
    if (this.busy() || !this.requestToken) return;
    const address = email.value.trim();
    this.busy.set(true);
    this.error.set('');
    this.api
      .invite(this.requestToken, address)
      .pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false)))
      .subscribe({
        next: (invitation) => {
          email.value = '';
          this.sent.set({ email: address, expiresAt: invitation.expiresAt });
          this.view.set('sent');
        },
        error: (error: HttpErrorResponse) => {
          if (error.status === 401) {
            this.deny(error);
            return;
          }
          this.error.set(messages[problemCode(error.error) ?? ''] ?? 'The invitation could not be sent. Try again.');
        },
      });
  }

  protected inviteAnother() {
    this.sent.set(null);
    this.view.set('form');
  }

  private deny(error: HttpErrorResponse) {
    this.view.set('denied');
    this.error.set(
      error.status === 401
        ? 'Sign in with your provider account and authenticator to invite administrators.'
        : (messages[problemCode(error.error) ?? ''] ?? 'Administrator invitations are unavailable.'),
    );
  }
}
