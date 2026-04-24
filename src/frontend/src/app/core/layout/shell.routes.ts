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
			provideEffects(
				ShopsEffects,
				UsersEffects,
				SuppliersEffects,
				InventoryEffects,
				CustomersEffects,
				SalesEffects,
				ExpensesEffects
			),
		],
		children: [
			{
				path: 'sales/new',
				loadComponent: () =>
					import('../../features/sales/pages/new-sale-page.component').then(
						(m) => m.NewSalePageComponent
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
				path: 'inventory',
				loadComponent: () =>
					import('../../features/inventory/pages/inventory-page.component').then(
						(m) => m.InventoryPageComponent
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
				path: 'customers/:customerId/account',
				loadComponent: () =>
					import('../../features/customers/pages/customer-account-page.component').then(
						(m) => m.CustomerAccountPageComponent
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
				path: '',
				pathMatch: 'full',
				redirectTo: 'users',
			},
		],
	},
];
