import { Routes } from '@angular/router';
import { provideEffects } from '@ngrx/effects';
import { provideState } from '@ngrx/store';

import { ShellComponent } from './shell.component';
import { ShopsEffects } from '../../features/shops/state/shops.effects';
import { shopsFeature } from '../../features/shops/state/shops.reducer';
import { SuppliersEffects } from '../../features/suppliers/state/suppliers.effects';
import { suppliersFeature } from '../../features/suppliers/state/suppliers.reducer';
import { UsersEffects } from '../../features/users/state/users.effects';
import { usersFeature } from '../../features/users/state/users.reducer';
import { InventoryEffects } from '../../features/inventory/state/inventory.effects';
import { inventoryFeature } from '../../features/inventory/state/inventory.reducer';
import { CustomersEffects } from '../../features/customers/state/customers.effects';
import { customersFeature } from '../../features/customers/state/customers.reducer';
import { SalesEffects } from '../../features/sales/state/sales.effects';
import { salesFeature } from '../../features/sales/state/sales.reducer';
import { ExpensesEffects } from '../../features/expenses/state/expenses.effects';
import { expensesFeature } from '../../features/expenses/state/expenses.reducer';
import { BankAccountsEffects } from '../../features/bank-accounts/state/bank-accounts.effects';
import { bankAccountsFeature } from '../../features/bank-accounts/state/bank-accounts.reducer';
import { PurchaseOrdersEffects } from '../../features/purchase-orders/state/purchase-orders.effects';
import { purchaseOrdersFeature } from '../../features/purchase-orders/state/purchase-orders.reducer';
import { discountsGuard } from '../guards/discounts.guard';
import { authGuard } from '../guards/auth.guard';
import { dashboardGuard } from '../guards/dashboard.guard';
import { profitLossGuard } from '../guards/profit-loss.guard';

export const shellRoutes: Routes = [
	{
		path: '',
		component: ShellComponent,
		providers: [
			provideState(shopsFeature),
			provideState(usersFeature),
			provideState(suppliersFeature),
			provideState(inventoryFeature),
			provideState(customersFeature),
			provideState(salesFeature),
			provideState(expensesFeature),
			provideState(bankAccountsFeature),
			provideState(purchaseOrdersFeature),
			provideEffects(
				ShopsEffects,
				UsersEffects,
				SuppliersEffects,
				InventoryEffects,
				CustomersEffects,
				SalesEffects,
				ExpensesEffects,
				BankAccountsEffects,
				PurchaseOrdersEffects
			),
		],
		children: [
			{
				path: 'dashboard',
				canActivate: [dashboardGuard],
				loadComponent: () =>
					import('../../features/dashboard/pages/dashboard-page/dashboard-page.component').then(
						(m) => m.DashboardPageComponent
					),
			},
			{
				path: 'sales/new',
				canActivate: [authGuard],
				data: { allowOfflineSalesGrace: true },
				loadComponent: () =>
					import('../../features/sales/pages/new-sale-page.component').then(
						(m) => m.NewSalePageComponent
					),
			},
			{
				path: 'sales/profit-loss',
				canActivate: [profitLossGuard],
				loadComponent: () =>
					import('../../features/sales/pages/profit-loss-page/profit-loss-page.component').then(
						(m) => m.ProfitLossPageComponent
					),
			},
			{
				path: 'sales/credit-notes',
				loadComponent: () =>
					import('../../features/sales/pages/credit-notes-page/credit-notes-page.component').then(
						(m) => m.CreditNotesPageComponent
					),
			},
			{
				path: 'sales',
				loadComponent: () =>
					import('../../features/sales/pages/sales-page.component').then(
						(m) => m.SalesPageComponent
					),
			},
			{
				path: 'expenses',
				loadComponent: () =>
					import('../../features/expenses/pages/expenses-page.component').then(
						(m) => m.ExpensesPageComponent
					),
			},
			{
				path: 'bank-accounts',
				loadChildren: () =>
					import('../../features/bank-accounts/bank-accounts.routes').then(
						(m) => m.bankAccountsRoutes
					),
			},
			{
				path: 'inventory/purchase-orders/builder',
				redirectTo: '/inventory/purchase-orders',
			},
			{
				path: 'inventory/purchase-orders/:purchaseOrderId',
				loadComponent: () =>
					import('../../features/purchase-orders/pages/purchase-order-detail-page.component').then(
						(m) => m.PurchaseOrderDetailPageComponent
					),
			},
			{
				path: 'inventory/purchase-orders',
				loadComponent: () =>
					import('../../features/purchase-orders/pages/purchase-orders-list-page.component').then(
						(m) => m.PurchaseOrdersListPageComponent
					),
			},
			{
				path: 'inventory/batch',
				loadComponent: () =>
					import('../../features/inventory/pages/inventory-batch-page.component').then(
						(m) => m.InventoryBatchPageComponent
					),
			},
			{
				path: 'inventory/batches',
				loadComponent: () =>
					import(
						'../../features/inventory/pages/inventory-batches-list-page/inventory-batches-list-page.component'
					).then((m) => m.InventoryBatchesListPageComponent),
			},
			{
				path: 'inventory/adjustments',
				loadComponent: () =>
					import(
						'../../features/inventory/pages/inventory-adjustments-page/inventory-adjustments-page.component'
					).then((m) => m.InventoryAdjustmentsPageComponent),
			},
			{
				path: 'inventory',
				loadComponent: () =>
					import('../../features/inventory/pages/inventory-page.component').then(
						(m) => m.InventoryPageComponent
					),
			},
			{
				path: 'services',
				loadComponent: () =>
					import('../../features/services/pages/services-page.component').then(
						(m) => m.ServicesPageComponent
					),
			},
			{
				path: 'suppliers',
				loadComponent: () =>
					import('../../features/suppliers/pages/suppliers-page.component').then(
						(m) => m.SuppliersPageComponent
					),
			},
			{
				path: 'customers',
				loadComponent: () =>
					import('../../features/customers/pages/customers-page.component').then(
						(m) => m.CustomersPageComponent
					),
			},
			{
				path: 'users',
				loadComponent: () =>
					import('../../features/users/pages/users-page.component').then(
						(m) => m.UsersPageComponent
					),
			},
			{
				path: 'discounts',
				canActivate: [discountsGuard],
				loadComponent: () =>
					import('../../features/discounts/pages/discounts-page.component').then(
						(m) => m.DiscountsPageComponent
					),
			},
			{
				path: '',
				pathMatch: 'full',
				redirectTo: 'dashboard',
			},
		],
	},
];
