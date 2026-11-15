import { Routes } from '@angular/router';

export const billingRoutes: Routes = [
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
