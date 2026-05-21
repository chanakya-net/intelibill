import { isPlatformBrowser } from '@angular/common';
import {
  APP_INITIALIZER,
  ApplicationConfig,
  Injector,
  PLATFORM_ID,
  inject,
  provideBrowserGlobalErrorListeners,
  isDevMode,
} from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';
import { definePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';
import { provideStore } from '@ngrx/store';
import { provideStoreDevtools } from '@ngrx/store-devtools';
import { providePrimeNG } from 'primeng/config';

import { routes } from './app.routes';
import { metaReducers, rootReducers } from './core/state';
import { provideClientHydration, withEventReplay } from '@angular/platform-browser';
import { provideServiceWorker } from '@angular/service-worker';
import { firstValueFrom } from 'rxjs';
import { provideTransloco, translocoConfig } from '@ngneat/transloco';

import { AuthService } from './core/auth/auth.service';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { httpLoadingInterceptor } from './core/interceptors/http-loading.interceptor';
import { LocalizationService } from './core/i18n/localization.service';
import { DEFAULT_LANGUAGE, SUPPORTED_LANGUAGES } from './core/i18n/language.constants';
import { TranslocoHttpLoader } from './core/i18n/transloco-http.loader';
import { ProductSignalRService } from './core/services/product-signalr.service';

const enterprisePreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#fff7ed',
      100: '#ffedd5',
      200: '#fed7aa',
      300: '#fdba74',
      400: '#fb923c',
      500: '#f97316',
      600: '#ea580c',
      700: '#c2410c',
      800: '#9a3412',
      900: '#7c2d12',
      950: '#431407',
    },
  },
});

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideAnimationsAsync(),
    provideRouter(routes),
    provideHttpClient(withFetch(), withInterceptors([httpLoadingInterceptor, authInterceptor])),
    providePrimeNG({
      ripple: true,
      theme: {
        preset: enterprisePreset,
        options: {
          darkModeSelector: false,
          cssLayer: {
            name: 'primeng',
            order: 'theme, base, primeng',
          },
        },
      },
    }),
    provideStore(rootReducers, { metaReducers }),
    provideStoreDevtools({
      maxAge: 25,
      logOnly: !isDevMode(),
    }),
    provideTransloco({
      config: translocoConfig({
        availableLangs: [...SUPPORTED_LANGUAGES],
        defaultLang: DEFAULT_LANGUAGE,
        fallbackLang: DEFAULT_LANGUAGE,
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
      }),
      loader: TranslocoHttpLoader,
    }),
    provideClientHydration(withEventReplay()),
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
    {
      provide: APP_INITIALIZER,
      multi: true,
      useFactory: initializeAppServices,
    },
  ],
};

function initializeAppServices(): () => Promise<void> {
  const localizationService = inject(LocalizationService);
  const authService = inject(AuthService);
  const injector = inject(Injector);
  const platformId = inject(PLATFORM_ID);

  return async () => {
    await localizationService.initialize();
    await firstValueFrom(authService.bootstrapSession());

    if (isPlatformBrowser(platformId)) {
      await injector.get(ProductSignalRService).startConnection();
    }
  };
}
