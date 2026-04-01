import { CommonModule, isPlatformBrowser } from '@angular/common';
import { Component, OnInit, PLATFORM_ID, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TimeoutError, take, timeout } from 'rxjs';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';

@Component({
  selector: 'app-auth-callback-page',
  standalone: true,
  imports: [CommonModule, RouterLink, ButtonModule, ProgressSpinnerModule],
  templateUrl: './auth-callback.component.html',
})
export class AuthCallbackComponent implements OnInit {
  private static readonly ExternalErrorStorageKey = 'inventory.auth.external.error';
  private static readonly ExternalPendingStorageKey = 'inventory.auth.external.pending';

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);
  private readonly platformId = inject(PLATFORM_ID);

  readonly isBusy = signal(true);
  readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    if (this.authService.isAuthenticated()) {
      this.clearExternalPendingMarker();
      void this.router.navigateByUrl('/');
      return;
    }

    this.route.queryParamMap.pipe(take(1)).subscribe((params) => {
      const providerError = params.get('error');
      if (providerError) {
        const providerErrorDescription = params.get('error_description');
        this.fail(providerErrorDescription ?? 'External provider returned an error. Please try again.');
        return;
      }

      const code = params.get('code') ?? '';
      const state = params.get('state') ?? '';

      if (!code || !state) {
        this.fail('Missing callback code or state. Please retry sign-in.');
        return;
      }

      this.authService
        .completeExternalLogin(code, state)
        .pipe(timeout(15000))
        .subscribe({
          next: () => {
            if (!this.authService.isAuthenticated()) {
              this.fail('Sign-in completed but no session was established. Please try again.');
              return;
            }

            this.clearExternalPendingMarker();

            void this.router.navigateByUrl('/');
          },
          error: (error: { error?: ApiErrorPayload } | TimeoutError) => {
            if (error instanceof TimeoutError) {
              this.fail('Sign-in timed out. Please try again.');
              return;
            }

            this.fail(mapCallbackError(error.error));
          },
        });
    });
  }

  private fail(message: string): void {
    this.errorMessage.set(message);
    this.isBusy.set(false);

    try {
      sessionStorage.setItem(AuthCallbackComponent.ExternalErrorStorageKey, message);
    } catch {
      // Ignore storage failures and fall back to query parameter only.
    }

    this.clearExternalPendingMarker();

    void this.router.navigateByUrl(`/login?externalAuthError=${encodeURIComponent(message)}`);
  }

  private clearExternalPendingMarker(): void {
    try {
      sessionStorage.removeItem(AuthCallbackComponent.ExternalPendingStorageKey);
    } catch {
      // Ignore storage failures.
    }
  }
}

function mapCallbackError(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Auth.ExternalStateInvalid') {
    return 'Your sign-in session expired. Please try again.';
  }

  if (title === 'Auth.UnsupportedProvider') {
    return 'This login provider is not enabled.';
  }

  return 'Unable to complete external sign-in.';
}
