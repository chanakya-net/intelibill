import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { RootState } from '../../../core/state/app.state';

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    InputTextModule,
    PasswordModule,
    ButtonModule,
    RouterLink,
    ProgressSpinnerModule,
  ],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.scss',
})
export class LoginPageComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly store = inject(Store<RootState>);

  readonly serverError = signal('');
  readonly isHttpLoading = this.store.selectSignal((state) => state.httpUi.pendingRequests > 0);

  readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
    rememberMe: [true],
  });

  ngOnInit(): void {
    if (this.authService.isAuthenticated()) {
      void this.router.navigateByUrl('/');
      return;
    }

    const rememberedEmail = this.authService.getLastRememberedEmail();
    if (rememberedEmail) {
      this.form.controls.email.setValue(rememberedEmail);
      this.form.controls.rememberMe.setValue(true);
    }
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.serverError.set('');

    const { email, password, rememberMe } = this.form.getRawValue();

    this.authService.loginWithEmail(email.trim(), password, rememberMe).subscribe({
      next: () => {
        void this.router.navigateByUrl('/');
      },
      error: (error: { error?: ApiErrorPayload }) => {
        this.serverError.set(getAuthErrorMessage(error.error));
      },
    });
  }
}

function getAuthErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Auth.InvalidCredentials') {
    return 'The email or password is incorrect.';
  }

  if (error?.detail) {
    return error.detail;
  }

  return 'Unable to sign in right now. Please try again.';
}
