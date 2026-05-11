import { Routes } from '@angular/router';

import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
	{
		path: 'login',
		loadComponent: () =>
			import('./features/auth/pages/login-page.component').then(
				(m) => m.LoginPageComponent
			),
	},
	{
		path: 'forgot-password',
		loadComponent: () =>
			import('./features/auth/pages/forgot-password-page.component').then(
				(m) => m.ForgotPasswordPageComponent
			),
	},
	{
		path: 'register',
		loadChildren: () =>
			import('./features/auth/register.routes').then((m) => m.registerRoutes),
	},
	{
		path: 'reset-password',
		loadComponent: () =>
			import('./features/auth/pages/reset-password-page.component').then(
				(m) => m.ResetPasswordPageComponent
			),
	},
	{
		path: 'auth/callback',
		loadComponent: () =>
			import('./features/auth/pages/auth-callback.component').then(
				(m) => m.AuthCallbackComponent
			),
	},
	{
		path: '',
		canActivate: [authGuard],
		loadChildren: () =>
			import('./core/layout/shell.routes').then((m) => m.shellRoutes),
	},
	{
		path: '**',
		loadComponent: () =>
			import('./features/not-found/pages/not-found-page.component').then(
				(m) => m.NotFoundPageComponent
			),
	},
];
