import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { EMPTY, finalize, switchMap } from 'rxjs';
import { AdminSessionService } from '../auth/admin-session.service';
import { AdminInvitationsApi } from './admin-invitations-api.service';

type View = 'loading' | 'form' | 'sent' | 'denied' | 'uncertain';

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
  private readonly session = inject(AdminSessionService);
  private readonly destroy = inject(DestroyRef);
  private requestToken?: string;

  protected readonly view = signal<View>('loading');
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  protected readonly sent = signal<{ email: string; expiresAt: string } | null>(null);

  constructor() {
    effect(() => {
      const notice = this.session.notice();
      if (notice === 'none') return;
      this.hideForm(notice === 'ended' ? 'denied' : 'uncertain');
    });
    this.api.ownerContext().pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: (context) => this.showForm(context.requestToken),
      error: (error: HttpErrorResponse) => this.deny(error),
    });
  }

  protected recover() {
    this.view.set('loading');
    this.error.set('');
    this.session.check(true).pipe(
      switchMap((outcome) => {
        if (outcome === 'active') return this.api.ownerContext();
        this.hideForm(outcome === 'ended' ? 'denied' : 'uncertain');
        return EMPTY;
      }),
      takeUntilDestroyed(this.destroy),
    ).subscribe({
      next: (context) => this.showForm(context.requestToken),
      error: (error: HttpErrorResponse) => this.deny(error),
    });
  }

  private showForm(token?: string) {
    if (this.session.notice() !== 'none') return;
    this.requestToken = token;
    this.view.set(token ? 'form' : 'denied');
  }

  protected submit(event: SubmitEvent, email: HTMLInputElement) {
    event.preventDefault();
    if (this.busy() || this.session.notice() !== 'none' || !this.requestToken) return;
    const address = email.value.trim();
    this.busy.set(true);
    this.error.set('');
    this.api
      .invite(this.requestToken, address)
      .pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false)))
      .subscribe({
        next: (invitation) => {
          if (this.session.notice() !== 'none') return;
          email.value = '';
          this.sent.set({ email: address, expiresAt: invitation.expiresAt });
          this.view.set('sent');
        },
        error: (error: HttpErrorResponse) => {
          if (error.status === 401 || this.session.notice() !== 'none') {
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

  private hideForm(view: 'denied' | 'uncertain') {
    this.requestToken = undefined;
    this.sent.set(null);
    this.view.set(view);
    this.error.set(view === 'uncertain'
      ? 'We could not confirm your provider session. Check your connection and try again.'
      : 'Sign in with your provider account and authenticator to invite administrators.');
  }

  private deny(error: HttpErrorResponse) {
    if (this.session.notice() === 'uncertain' || this.session.notice() === 'expired') {
      this.hideForm('uncertain');
      return;
    }
    this.hideForm('denied');
    if (error.status === 401 || this.session.notice() === 'ended') return;
    const detail = messages[problemCode(error.error) ?? ''];
    const fallback = error.status === 403
      ? 'You cannot manage administrator invitations.'
      : 'Administrator invitations are unavailable.';
    this.error.set(detail ?? fallback);
  }
}
