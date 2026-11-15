import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { finalize } from 'rxjs';
import { AdminSessionService } from '../admin-session.service';
import { AdminRecoveryApi, type IssuedRecoveryReset, type RecoveryReview } from './admin-recovery-api.service';

const messages: Readonly<Record<string, string>> = {
  'Admin.RecoveryReviewInvalid': 'Select the reason and verification, enter a ticket-style reference and confirm the review.',
  'Admin.RecoveryTargetUnavailable': 'No active administrator uses that email.',
  'Admin.LastRecoverableOwner': 'This reset would leave no platform owner able to recover access.',
  'Admin.RecoverySelfReview': 'Use your own recovery codes to recover your access.',
  'Admin.RecentMfaRequired': 'Sign in again with your authenticator, then review the recovery within five minutes.',
  'Admin.AccessDenied': 'You cannot review administrator recovery.',
  'Admin.Throttled': 'Too many attempts. Try again later.',
  'Admin.RequestRejected': 'This request expired. Reload the page and try again.',
};

/**
 * A platform owner resets another administrator's MFA on explicit evidence. The one-use reset code is
 * shown once, for out-of-band handover to the verified person; it never appears in audit history.
 */
@Component({
  selector: 'adm-review-recovery-page',
  imports: [ButtonDirective, DatePipe, RouterLink],
  templateUrl: './review-recovery-page.component.html',
  styleUrl: '../../form-page.css',
  styles: `select { min-height: 2.75rem; }`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReviewRecoveryPageComponent {
  private readonly api = inject(AdminRecoveryApi);
  private readonly session = inject(AdminSessionService);
  private readonly destroy = inject(DestroyRef);
  private requestToken = '';

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly issued = signal<(IssuedRecoveryReset & { email: string }) | null>(null);

  constructor() {
    // Hide a shown reset code as soon as this tab's provider session ends.
    effect(() => {
      if (this.session.state() !== 'authenticated') this.issued.set(null);
    });
    this.api.context().pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: ({ requestToken }) => (this.requestToken = requestToken),
      error: () => this.error.set('Recovery review is unavailable. Reload the page.'),
    });
  }

  protected submit(event: SubmitEvent, form: HTMLFormElement) {
    event.preventDefault();
    if (this.busy() || !this.requestToken) return;
    const data = new FormData(form);
    const review = {
      email: String(data.get('email') ?? '').trim(),
      reason: data.get('reason'),
      verification: data.get('verification'),
      evidenceReference: String(data.get('evidenceReference') ?? '').trim(),
      confirmed: data.get('confirmed') === 'on',
    } as RecoveryReview;
    this.busy.set(true);
    this.error.set('');
    this.api.review(this.requestToken, review)
      .pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false)))
      .subscribe({
        next: (issued) => {
          form.reset();
          this.issued.set({ ...issued, email: review.email });
        },
        error: (error: HttpErrorResponse) => this.error.set(messages[problemCode(error.error) ?? '']
          ?? (error.status === 401 ? 'Your provider session ended. Sign in again.' : 'The reset could not be issued. Try again.')),
      });
  }

  protected done() {
    this.issued.set(null);
  }
}
