import { Routes } from '@angular/router';

export const billingRoutes: Routes = [
  {
    path: 'accounts/:accountId/coverage',
    loadComponent: () => import('./affiliations/account-coverage.component').then((m) => m.AccountCoverageComponent),
  },
  {
    path: 'consent/:accountId/:shopId/:requestId',
    loadComponent: () => import('./affiliations/owner-consent.component').then((m) => m.OwnerConsentComponent),
  },
  {
    path: 'invitations',
    loadComponent: () =>
      import('./members/billing-inbox.component').then((m) => m.BillingInboxComponent),
  },
  {
    path: 'accounts/:accountId/members',
    loadComponent: () =>
      import('./members/billing-members.component').then((m) => m.BillingMembersComponent),
  },
  {
    path: '',
    loadComponent: () =>
      import('./pages/billing-accounts-page.component').then((m) => m.BillingAccountsPageComponent),
  },
];
