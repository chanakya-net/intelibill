import { identityError } from '../../../core/auth/identity-errors';
import { Location } from '@angular/common';
import { IdentityFlowService } from '../../../core/auth/identity-flow.service';
import { Component, OnInit, inject, signal } from '@angular/core';
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

  private readonly flow = inject(IdentityFlowService);
  private readonly location = inject(Location);

  readonly isBusy = signal(true);
  readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    if (this.flow.hasOAuthIntent()) {
      this.route.queryParamMap.pipe(take(1)).subscribe((params) => {
        const code = params.get('code');
        const state = params.get('state');
        this.location.replaceState('/auth/callback');
        if (!code || !state || params.get('error')) {
          this.failIdentity();
          return;
        }
        this.flow
          .completeOAuth(code, state)
          .pipe(timeout(15000))
          .subscribe({
            next: (result) => {
              if (result === 'redirecting') return;
              void this.router.navigateByUrl(
                result === 'security-updated'
                  ? '/login?identity=updated'
                  : '/account/security?identity=pending',
              );
            },
            error: (error) => this.failIdentity(error),
          });
      });
      return;
    }
    if (this.authService.isAuthenticated()) {
      this.clearExternalPendingMarker();
      void this.router.navigateByUrl('/');
      return;
    }

    this.route.queryParamMap.pipe(take(1)).subscribe((params) => {
      this.location.replaceState('/auth/callback');
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

            void this.router.navigateByUrl(this.flow.afterSignIn());
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

  private failIdentity(error?: Parameters<typeof identityError>[0]): void {
    this.flow.clearOAuth();
    this.isBusy.set(false);
    const reason = error && identityError(error) === 'identity.collision' ? 'collision' : 'failed';
    this.errorMessage.set(reason === 'collision' ? 'identity.collision' : 'identity.reauthFailed');
    void this.router.navigateByUrl(
      `${this.authService.isAuthenticated() ? '/account/security' : '/login'}?identity=${reason}`,
    );
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
  const title = error?.code ?? error?.title ?? '';
  if (title === 'Auth.AccountLinkRequired') return 'identity.collision';

  if (title === 'Auth.ExternalStateInvalid') {
    return 'errors.auth.externalStateInvalid';
  }

  if (title === 'Auth.UnsupportedProvider') {
    return 'errors.auth.unsupportedProvider';
  }

  return 'errors.auth.unableToCompleteExternalSignIn';
}
