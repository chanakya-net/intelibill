import { type ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';
import { SESSION_PORT } from '@intelibill/api';
import { INTELIBILL_PRIME_CSS_LAYER, INTELIBILL_PRIME_PRESET } from '@intelibill/ui/theme';
import { providePrimeNG } from 'primeng/config';
import { unavailableProviderSession } from './unavailable-provider-session';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideAnimationsAsync(),
    providePrimeNG({
      ripple: true,
      theme: {
        preset: INTELIBILL_PRIME_PRESET,
        options: {
          darkModeSelector: false,
          cssLayer: INTELIBILL_PRIME_CSS_LAYER,
        },
      },
    }),
    { provide: SESSION_PORT, useValue: unavailableProviderSession },
    provideRouter([
      {
        path: '',
        loadComponent: () => import('./landing-page.component').then((m) => m.LandingPageComponent),
      },
      { path: '**', redirectTo: '' },
    ]),
  ],
};
