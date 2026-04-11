import { isPlatformBrowser } from '@angular/common';
import { Component, OnInit, PLATFORM_ID, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TimeoutError, take, timeout } from 'rxjs';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';

@Component({
  selector: 'app-auth-callback-page',
  standalone: true,
  imports: [RouterLink, ButtonModule, ProgressSpinnerModule, TranslocoPipe],
  templateUrl: './auth-callback.component.html',
  styleUrl: './auth-callback.component.scss',
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
        this.fail('errors.auth.externalProviderError');
        return;
      }

      const code = params.get('code') ?? '';
      const state = params.get('state') ?? '';

      if (!code || !state) {
        this.fail('errors.auth.missingCallbackData');
        return;
      }

      this.authService
        .completeExternalLogin(code, state)
        .pipe(timeout(15000))
        .subscribe({
          next: () => {
            if (!this.authService.isAuthenticated()) {
              this.fail('errors.auth.noSessionEstablished');
              return;
            }

            this.clearExternalPendingMarker();

            void this.router.navigateByUrl('/');
          },
          error: (error: { error?: ApiErrorPayload } | TimeoutError) => {
            if (error instanceof TimeoutError) {
              this.fail('errors.auth.signInTimeout');
              return;
            }

            this.fail(mapCallbackError(error.error));
          },
        });
    });
  }

  private fail(messageKey: string): void {
    this.errorMessage.set(messageKey);
    this.isBusy.set(false);

    try {
      sessionStorage.setItem(AuthCallbackComponent.ExternalErrorStorageKey, messageKey);
    } catch {
      // Ignore storage failures.
    }

    this.clearExternalPendingMarker();

    void this.router.navigateByUrl(`/login?externalAuthError=${encodeURIComponent(messageKey)}`);
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
    return 'errors.auth.externalStateInvalid';
  }

  if (title === 'Auth.UnsupportedProvider') {
    return 'errors.auth.unsupportedProvider';
  }

  return 'errors.auth.unableToCompleteExternalSignIn';
}
