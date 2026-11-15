import { Routes } from '@angular/router';

export const billingRoutes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./pages/billing-accounts-page.component').then((m) => m.BillingAccountsPageComponent),
  },
];
