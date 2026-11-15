import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { finalize } from 'rxjs';
import { AdminSessionService } from '../admin-session.service';
import { AdminRecoveryApi, type RecoveryProof } from './admin-recovery-api.service';

type Method = 'code' | 'reset';

const messages: Readonly<Record<string, string>> = {
  'Admin.RecoveryRefused': 'Check your email, password and recovery proof. A used code or reset never works twice.',
  'Admin.Throttled': 'Too many attempts. Try again later.',
  'Admin.RequestRejected': 'This recovery request expired. Try again.',
  'Admin.RequestTooLarge': 'The request is too large.',
};

/**
 * Recovers access with a recovery code or an owner-reviewed reset. Success ends every session of the
 * account and leads only to authenticator enrollment: no provider access returns before it.
 */
@Component({
  selector: 'adm-recover-access-page',
  imports: [ButtonDirective, RouterLink],
  templateUrl: './recover-access-page.component.html',
  styleUrl: '../../form-page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecoverAccessPageComponent {
  private readonly api = inject(AdminRecoveryApi);
  private readonly session = inject(AdminSessionService);
  private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef);
  private requestToken = '';

  protected readonly method = signal<Method>('code');
  protected readonly ready = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  constructor() {
    this.prepare();
  }

  protected prepare() {
    this.error.set('');
    this.api.context().pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: ({ requestToken }) => {
        this.requestToken = requestToken;
        this.ready.set(true);
      },
      error: () => this.error.set('Provider recovery is unavailable. Try again.'),
    });
  }

  protected choose(method: Method) {
    this.method.set(method);
    this.error.set('');
  }

  protected submit(event: SubmitEvent, email: HTMLInputElement, password: HTMLInputElement, proof: HTMLInputElement) {
    event.preventDefault();
    if (this.busy() || !this.ready()) return;
    const value = proof.value.trim();
    const recovery: RecoveryProof = this.method() === 'code'
      ? { kind: 'code', recoveryCode: value } : { kind: 'reset', resetToken: value };
    const request = this.api.redeem(this.requestToken, email.value.trim(), password.value, recovery);
    password.value = '';
    proof.value = '';
    this.busy.set(true);
    this.error.set('');
    request.pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false))).subscribe({
      next: () => {
        // The server ended every session of this account; hide anything this tab still shows.
        this.session.settle('ended');
        void this.router.navigateByUrl('/auth/mfa-enrollment');
      },
      error: (error: HttpErrorResponse) => this.failed(error),
    });
  }

  private failed(error: HttpErrorResponse) {
    const code = problemCode(error.error) ?? '';
    if (error.status === 403) {
      this.ready.set(false);
      this.requestToken = '';
    }
    this.error.set(messages[code] ?? 'Recovery could not be completed. Try again.');
  }
}
