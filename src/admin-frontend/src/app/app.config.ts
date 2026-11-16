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
        path: 'security/sessions',
        canActivate: [requireAdminSession],
        loadComponent: () => import('./features/security/sessions/sessions-page.component')
          .then((m) => m.SessionsPageComponent),
      },
      {
        path: 'security/recovery',
        canActivate: [requireAdminSession],
        loadComponent: () => import('./features/auth/recovery/review-recovery-page.component')
          .then((m) => m.ReviewRecoveryPageComponent),
      },
      {
        path: 'audit',
        canActivate: [requireAdminSession],
        loadComponent: () => import('./features/audit/audit-page.component').then((m) => m.AuditPageComponent),
      },
      {
        path: 'accounts',
        canActivate: [requireAdminSession],
        loadComponent: () => import('./features/accounts/account-status-page.component')
          .then((m) => m.AccountStatusPageComponent),
      },
      {
        path: 'catalogue',
        canActivate: [requireAdminSession],
        loadComponent: () => import('./features/catalogue/catalogue-page.component')
          .then((m) => m.CataloguePageComponent),
      },
      {
        path: 'auth/sign-in',
        loadComponent: () => import('./features/auth/sign-in-page.component').then((m) => m.SignInPageComponent),
      },
      {
        path: 'auth/recovery',
        loadComponent: () => import('./features/auth/recovery/recover-access-page.component')
          .then((m) => m.RecoverAccessPageComponent),
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
        path: 'admin-users',
        canActivate: [requireAdminSession],
        loadComponent: () => import('./features/admin-users/admin-capabilities-page.component')
          .then((m) => m.AdminCapabilitiesPageComponent),
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
