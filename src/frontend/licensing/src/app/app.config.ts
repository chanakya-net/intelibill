import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { type ApplicationConfig, isDevMode, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';
import { SESSION_PORT } from '@intelibill/api';
import { INTELIBILL_PRIME_CSS_LAYER, INTELIBILL_PRIME_PRESET } from '@intelibill/ui/theme';
import { provideTransloco, translocoConfig } from '@ngneat/transloco';
import { providePrimeNG } from 'primeng/config';
import { adminSessionInterceptor } from './core/auth/admin-session.interceptor';
import { ADMIN_DEFAULT_LANGUAGE, ADMIN_SUPPORTED_LANGUAGE_CODES } from './core/i18n/admin-languages';
import { AdminTranslocoLoader } from './core/i18n/admin-transloco.loader';
import { AdminSessionService } from './features/auth/admin-session.service';
import { requireAdminSession } from './features/auth/require-admin-session';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideAnimationsAsync(),
    provideHttpClient(withInterceptors([adminSessionInterceptor])),
    provideTransloco({
      config: translocoConfig({
        availableLangs: [...ADMIN_SUPPORTED_LANGUAGE_CODES],
        defaultLang: ADMIN_DEFAULT_LANGUAGE,
        fallbackLang: ADMIN_DEFAULT_LANGUAGE,
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
      }),
      loader: AdminTranslocoLoader,
    }),
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
        path: 'payments/review',
        canActivate: [requireAdminSession],
        loadComponent: () => import('./features/payments/review/payment-review-page.component')
          .then((m) => m.PaymentReviewPageComponent),
      },
      {
        path: 'payments/refunds/:account/:request/approve',
        canActivate: [requireAdminSession],
        loadComponent: () => import('./features/refunds/refund-approval-page.component')
          .then((m) => m.RefundApprovalPageComponent),
      },
      {
        path: 'payments/refunds',
        canActivate: [requireAdminSession],
        loadComponent: () => import('./features/payments/refunds/refund-intake-page.component')
          .then((m) => m.RefundIntakePageComponent),
      },
      {
        path: 'payments/documents',
        canActivate: [requireAdminSession],
        loadComponent: () => import('./features/documents/billing-document-page.component')
          .then((m) => m.BillingDocumentPageComponent),
      },
      {
        path: 'payments',
        canActivate: [requireAdminSession],
        loadComponent: () => import('./features/payments/manual-payment-page.component')
          .then((m) => m.ManualPaymentPageComponent),
      },
      {
        path: 'restrictions/reopening',
        canActivate: [requireAdminSession],
        loadComponent: () => import('./features/restrictions/reopening-review.component')
          .then((m) => m.ReopeningReviewComponent),
      },
      {
        path: 'restrictions',
        canActivate: [requireAdminSession],
        loadComponent: () => import('./features/restrictions/suspension-page.component')
          .then((m) => m.SuspensionPageComponent),
      },
      {
        path: 'coverage-exceptions',
        canActivate: [requireAdminSession],
        loadComponent: () => import('./features/coverage-exceptions/coverage-exception-page.component')
          .then((m) => m.CoverageExceptionPageComponent),
      },
      {
        path: 'accounts/:accountId/coverage-transfers',
        canActivate: [requireAdminSession],
        loadComponent: () => import('./features/coverage-exceptions/transfer-review-page.component')
          .then(m => m.TransferReviewPageComponent),
      },
      {
        path: 'accounts/:accountId/coverage-exceptions',
        canActivate: [requireAdminSession],
        loadComponent: () => import('./features/coverage-exceptions/coverage-exception-page.component')
          .then((m) => m.CoverageExceptionPageComponent),
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
