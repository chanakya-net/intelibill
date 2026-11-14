import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { type ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';
import { SESSION_PORT } from '@intelibill/api';
import { INTELIBILL_PRIME_CSS_LAYER, INTELIBILL_PRIME_PRESET } from '@intelibill/ui/theme';
import { providePrimeNG } from 'primeng/config';
import { adminSessionInterceptor } from './core/auth/admin-session.interceptor';
import { AdminSessionService } from './features/auth/admin-session.service';
import { requireAdminSession } from './features/auth/require-admin-session';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideAnimationsAsync(),
    provideHttpClient(withInterceptors([adminSessionInterceptor])),
    providePrimeNG({
      ripple: true,
      theme: {
        preset: INTELIBILL_PRIME_PRESET,
        options: {
          darkModeSelector: false,
          cssLayer: INTELIBILL_PRIME_CSS_LAYER,
        },
      },
    }),
    { provide: SESSION_PORT, useExisting: AdminSessionService },
    provideRouter([
      {
        path: '',
        pathMatch: 'full',
        canActivate: [requireAdminSession],
        loadComponent: () => import('./landing-page.component').then((m) => m.LandingPageComponent),
      },
      {
        path: 'auth/sign-in',
        loadComponent: () => import('./features/auth/sign-in-page.component').then((m) => m.SignInPageComponent),
      },
      {
        path: 'auth/bootstrap',
        loadComponent: () => import('./features/auth/bootstrap-page.component').then((m) => m.BootstrapPageComponent),
      },
      {
        path: 'auth/bootstrap/enrollment',
        data: { enrollment: true },
        loadComponent: () => import('./features/auth/bootstrap-page.component').then((m) => m.BootstrapPageComponent),
      },
      {
        path: 'admin-users/invite',
        loadComponent: () =>
          import('./features/admin-users/invite-admin-page.component').then((m) => m.InviteAdminPageComponent),
      },
      {
        path: 'auth/invitation',
        loadComponent: () =>
          import('./features/admin-users/accept-invitation-page.component').then(
            (m) => m.AcceptInvitationPageComponent,
          ),
      },
      {
        path: 'auth/mfa-enrollment',
        loadComponent: () => import('./features/auth/mfa-enrollment/mfa-enrollment-page.component')
          .then((m) => m.MfaEnrollmentPageComponent),
      },
      { path: '**', redirectTo: '' },
    ]),
  ],
};
