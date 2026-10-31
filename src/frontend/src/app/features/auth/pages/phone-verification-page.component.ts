import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { finalize } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { PhoneChallengeReceipt } from '../../../core/auth/auth.models';
import { identityError } from '../../../core/auth/identity-errors';
import { IdentityFlowService } from '../../../core/auth/identity-flow.service';
import { InputValidators } from '../../../shared/forms/input-validation';

/**
 * Phone sign-in and enrollment. Two steps: ask for the number, then confirm the code that was
 * sent to it. A wrong code never clears the step it was entered on, so the number and the
 * challenge the holder is part-way through survive a mistyped digit.
 */
@Component({
  selector: 'app-phone-verification-page',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, TranslocoPipe, ButtonModule, InputTextModule],
  templateUrl: './phone-verification-page.component.html',
  styleUrl: './phone-verification-page.component.scss',
})
export class PhoneVerificationPageComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly identityFlow = inject(IdentityFlowService);
  private readonly formBuilder = inject(FormBuilder);
  private ticker: ReturnType<typeof setInterval> | null = null;

  /** null until the server has answered: neither offered nor refused yet. */
  readonly available = signal<boolean | null>(null);
  readonly mode = signal<'login' | 'enrollment'>('login');
  readonly receipt = signal<PhoneChallengeReceipt | null>(null);
  readonly error = signal('');
  readonly busy = signal(false);
  private readonly now = signal(Date.now());

  readonly phoneForm = this.formBuilder.nonNullable.group({
    phoneNumber: ['', InputValidators.requiredText(16)],
  });

  readonly codeForm = this.formBuilder.nonNullable.group({
    code: ['', InputValidators.requiredText(10)],
    firstName: [''],
    lastName: [''],
  });

  /**
   * Driven by the deadline the server put in the receipt, not by when this page happened to render.
   * A reloaded page, or a slow delivery, must not offer a resend the server will refuse.
   */
  readonly resendSeconds = computed(() => this.secondsUntil(this.receipt()?.resendAllowedAt));

  readonly expirySeconds = computed(() => this.secondsUntil(this.receipt()?.expiresAt));

  readonly canResend = computed(() => this.resendSeconds() === 0 && !this.busy());

  ngOnInit(): void {
    this.auth.getPhoneAuthAvailability().subscribe({
      next: (availability) => this.available.set(availability.enabled),
      error: () => this.available.set(false),
    });

    this.ticker = setInterval(() => this.now.set(Date.now()), 1000);
  }

  ngOnDestroy(): void {
    if (this.ticker) clearInterval(this.ticker);
  }

  setMode(mode: 'login' | 'enrollment'): void {
    this.mode.set(mode);
    this.receipt.set(null);
    this.error.set('');
  }

  requestCode(): void {
    if (this.phoneForm.invalid) {
      this.phoneForm.markAllAsTouched();
      return;
    }
    if (this.busy()) return;

    this.busy.set(true);
    this.error.set('');
    this.auth
      .requestPhoneChallenge(this.phoneForm.getRawValue().phoneNumber, this.mode())
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: (receipt) => this.receipt.set(receipt),
        error: (error) => this.error.set(identityError(error)),
      });
  }

  confirmCode(): void {
    const receipt = this.receipt();
    if (!receipt || this.busy()) return;

    if (this.codeForm.controls.code.invalid || (this.mode() === 'enrollment' && !this.namesGiven())) {
      this.codeForm.markAllAsTouched();
      return;
    }

    const { code, firstName, lastName } = this.codeForm.getRawValue();
    this.busy.set(true);
    this.error.set('');

    const confirmation =
      this.mode() === 'login'
        ? this.auth.confirmPhoneLogin(receipt.challengeId, code.trim())
        : this.auth.confirmPhoneEnrollment({
            challengeId: receipt.challengeId,
            code: code.trim(),
            firstName: firstName.trim(),
            lastName: lastName.trim(),
          });

    confirmation.pipe(finalize(() => this.busy.set(false))).subscribe({
      next: () => {
        this.receipt.set(null);
        void this.router.navigateByUrl(this.identityFlow.afterSignIn());
      },
      error: (error) => {
        // Only the code is cleared. The number, the names and the challenge stay, because the
        // likely cause is a mistyped digit and retyping everything is the wrong penalty for that.
        this.error.set(identityError(error));
        this.codeForm.controls.code.reset();
      },
    });
  }

  private namesGiven(): boolean {
    const { firstName, lastName } = this.codeForm.getRawValue();
    return firstName.trim().length > 0 && lastName.trim().length > 0;
  }

  private secondsUntil(deadline: string | undefined): number {
    if (!deadline) return 0;
    const remaining = Date.parse(deadline) - this.now();
    return Number.isNaN(remaining) ? 0 : Math.max(0, Math.ceil(remaining / 1000));
  }
}
