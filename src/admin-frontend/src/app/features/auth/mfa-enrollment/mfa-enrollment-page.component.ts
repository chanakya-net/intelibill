import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { finalize, switchMap } from 'rxjs';

const path = '/api/admin/auth/bootstrap/enrollment/mfa';

@Component({
  selector: 'adm-mfa-enrollment-page',
  imports: [ButtonDirective, RouterLink],
  templateUrl: './mfa-enrollment-page.component.html',
  styleUrl: '../../form-page.css',
  host: { '(window:pagehide)': 'clearOnPageHide()' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MfaEnrollmentPageComponent {
  private readonly http = inject(HttpClient);
  private readonly destroy = inject(DestroyRef);
  private requestToken = '';

  protected readonly view = signal<'loading' | 'verify' | 'complete' | 'closed'>('loading');
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  protected readonly setupSecret = signal('');
  protected readonly recoveryCodes = signal<readonly string[]>([]);

  constructor() {
    this.http.get<{ requestToken: string }>(path).pipe(
      switchMap(({ requestToken }) => {
        this.requestToken = requestToken;
        return this.http.post<{ setupSecret: string }>(path + '/start', {}, this.csrf());
      }),
      takeUntilDestroyed(this.destroy),
    ).subscribe({
      next: ({ setupSecret }) => {
        if (this.view() !== 'loading') return;
        this.setupSecret.set(setupSecret);
        this.view.set('verify');
      },
      error: (error: HttpErrorResponse) => this.close(error),
    });
  }

  protected verify(event: SubmitEvent, input: HTMLInputElement) {
    event.preventDefault();
    if (this.busy() || this.view() !== 'verify') return;

    const otp = input.value.trim();
    input.value = '';
    this.busy.set(true);
    this.error.set('');
    this.http.post<{ recoveryCodes: readonly string[] }>(path + '/verify', { otp }, this.csrf()).pipe(
      takeUntilDestroyed(this.destroy),
      finalize(() => this.busy.set(false)),
    ).subscribe({
      next: ({ recoveryCodes }) => {
        if (this.view() !== 'verify') return;
        this.requestToken = '';
        this.setupSecret.set('');
        this.recoveryCodes.set(recoveryCodes);
        this.view.set('complete');
      },
      error: (error: HttpErrorResponse) => this.verificationFailed(error),
    });
  }

  protected dismissCodes() {
    this.recoveryCodes.set([]);
  }

  protected clearOnPageHide() {
    this.requestToken = '';
    this.setupSecret.set('');
    this.recoveryCodes.set([]);
    this.view.set('closed');
  }

  private verificationFailed(error: HttpErrorResponse) {
    if (error.status === 401 || error.status === 403) {
      this.close(error);
      return;
    }
    const code = problemCode(error.error);
    if (code === 'Mfa.InvalidProof') this.error.set('Enter a current code from your authenticator app.');
    else if (code === 'Mfa.Throttled') this.error.set('Too many attempts. Try again later.');
    else this.error.set('Verification could not be confirmed. Reload this page to check whether setup is still pending.');
  }

  private close(error: HttpErrorResponse) {
    this.requestToken = '';
    this.setupSecret.set('');
    this.view.set('closed');
    if (error.status !== 401) this.error.set('Authenticator setup is unavailable. Reload the page to try again.');
  }

  private csrf() {
    return { headers: { 'X-Bootstrap-CSRF': this.requestToken } };
  }
}
