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

export const shellRoutes: Routes = [
	{
		path: '',
		component: ShellComponent,
		providers: [
			provideState(shopsFeature),
			provideState(usersFeature),
			provideState(suppliersFeature),
			provideState(inventoryFeature),
			provideEffects(ShopsEffects, UsersEffects, SuppliersEffects, InventoryEffects),
		],
		children: [
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
