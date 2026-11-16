import { Routes } from '@angular/router';

export const billingRoutes: Routes = [
  {
    path: 'trial',
    loadComponent: () => import('./trial/shop-trial-page.component').then(m => m.ShopTrialPageComponent),
  },
  {
    path: 'accounts/:accountId/ownership-transfer',
    loadComponent: () => import('./members/billing-ownership-transfer.component').then(m => m.BillingOwnershipTransferComponent),
  },
  {
    path: 'ownership-transfers/:transferId',
    loadComponent: () => import('./members/billing-ownership-transfer.component').then(m => m.BillingOwnershipTransferComponent),
  },
  {
    path: 'ownership-transfers',
    loadComponent: () => import('./members/billing-ownership-inbox.component').then(m => m.BillingOwnershipInboxComponent),
  },
  {
    path: 'status',
    loadComponent: () => import('./status/subscription-status-page.component').then((m) => m.SubscriptionStatusPageComponent),
  },
  {
    path: 'accounts/:accountId/status',
    loadComponent: () => import('./status/subscription-status-page.component').then((m) => m.SubscriptionStatusPageComponent),
  },
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
    path: 'plans',
    loadComponent: () => import('./plans/billing-plans-page.component').then((m) => m.BillingPlansPageComponent),
  },
  {
    path: '',
    loadComponent: () =>
      import('./pages/billing-accounts-page.component').then((m) => m.BillingAccountsPageComponent),
  },
];
