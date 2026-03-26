import { Routes } from '@angular/router';
import { provideState } from '@ngrx/store';

import { authGuard } from './core/guards/auth.guard';
import { ShellComponent } from './core/layout/shell.component';
import { operationsFeature } from './features/operations/state/operations.feature';
import { overviewFeature } from './features/overview/state/overview.feature';

export const routes: Routes = [
	{
		path: 'login',
		loadComponent: () =>
			import('./features/auth/pages/login-page.component').then(
				(m) => m.LoginPageComponent
			),
	},
	{
		path: 'register',
		loadComponent: () =>
			import('./features/auth/pages/register-page.component').then(
				(m) => m.RegisterPageComponent
			),
	},
	{
		path: '',
		component: ShellComponent,
		canActivate: [authGuard],
		children: [
			{
				path: '',
				pathMatch: 'full',
				redirectTo: 'overview',
			},
			{
				path: 'overview',
				providers: [provideState(overviewFeature)],
				loadComponent: () =>
					import('./features/overview/pages/overview-page.component').then(
						(m) => m.OverviewPageComponent
					),
			},
			{
				path: 'operations',
				providers: [provideState(operationsFeature)],
				loadComponent: () =>
					import('./features/operations/pages/operations-page.component').then(
						(m) => m.OperationsPageComponent
					),
			},
		],
	},
	{
		path: '**',
		loadComponent: () =>
			import('./features/not-found/pages/not-found-page.component').then(
				(m) => m.NotFoundPageComponent
			),
	},
];
