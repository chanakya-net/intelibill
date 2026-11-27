import { Routes } from '@angular/router';
import { provideEffects } from '@ngrx/effects';

import { RegisterEffects } from './state/register.effects';

export const registerRoutes: Routes = [
	{
		path: '',
		loadComponent: () =>
			import('./pages/register-page.component').then((m) => m.RegisterPageComponent),
		providers: [provideEffects(RegisterEffects)],
	},
];
