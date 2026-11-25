import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { finalize, from } from 'rxjs';
import { AdminRefreshCoordinator } from '../../core/auth/admin-refresh-coordinator';

import { AdminSessionService } from './admin-session.service';

const path = '/api/admin/auth/sign-in';

@Component({
  selector: 'adm-sign-in-page',
  imports: [ButtonDirective, RouterLink],
  templateUrl: './sign-in-page.component.html',
  styleUrl: '../form-page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SignInPageComponent {
  private readonly http = inject(HttpClient);
  private readonly coordinator = inject(AdminRefreshCoordinator);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroy = inject(DestroyRef);
  private requestToken = '';

  protected readonly session = inject(AdminSessionService);
  protected readonly ready = signal(false);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  constructor() {
    this.prepare();
  }

  protected prepare() {
    this.error.set('');
    this.loading.set(true);
    this.http.get<{ requestToken: string }>(path + '/context').pipe(
      takeUntilDestroyed(this.destroy),
      finalize(() => this.loading.set(false)),
    ).subscribe({
      next: ({ requestToken }) => {
        this.requestToken = requestToken;
        this.ready.set(true);
      },
      error: () => this.error.set('Provider sign-in is unavailable. Try again.'),
    });
  }

  protected submit(event: SubmitEvent, email: HTMLInputElement, password: HTMLInputElement, otp: HTMLInputElement) {
    event.preventDefault();
    if (this.busy() || !this.ready()) return;
    const request = { email: email.value.trim(), password: password.value, otp: otp.value };
    password.value = '';
    otp.value = '';
    this.busy.set(true);
    this.error.set('');

    from(this.coordinator.signIn(request, this.requestToken)).pipe(
      takeUntilDestroyed(this.destroy),
      finalize(() => this.busy.set(false)),
    ).subscribe({
      next: () => this.finishSignIn(),
      error: (error: HttpErrorResponse) => this.signInFailed(error),
    });
  }

  private finishSignIn() {
    const params = this.route.snapshot.queryParamMap;
    const account = params.get('paymentAccount');
    const order = params.get('paymentOrder');
    const reviewAccount = params.get('reviewAccount');
    const refundAccount = params.get('refundAccount');
    const refundRequest = params.get('refundRequest');
    const invoiceAccount = params.get('invoiceAccount');
    const invoiceOrder = params.get('invoiceOrder');
    if (invoiceAccount && invoiceOrder) {
      void this.router.navigate(['/payments/documents'], { queryParams: { account: invoiceAccount, order: invoiceOrder } });
    } else if (refundAccount && refundRequest) {
      void this.router.navigate(['/payments/refunds', refundAccount, refundRequest, 'approve']);
    } else if (reviewAccount) {
      void this.router.navigate(['/payments/review'], { queryParams: { account: reviewAccount, case: params.get('reviewCase') } });
    } else if (account && order) {
      void this.router.navigate(['/payments'], { queryParams: { account, order } });
    } else {
      void this.router.navigateByUrl('/');
    }
  }

  private signInFailed(error: HttpErrorResponse) {
    if (error.status === 403) {
      this.ready.set(false);
      this.requestToken = '';
      this.error.set('This sign-in request expired. Try again.');
      return;
    }
    if (error.status === 429) {
      this.error.set('Too many attempts. Try again later.');
      return;
    }
    this.error.set(error.status === 401
      ? 'Check your email, password and current authenticator code.'
      : 'Provider sign-in is unavailable. Try again.');
  }
}
