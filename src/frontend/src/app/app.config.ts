import {
  APP_INITIALIZER,
  ApplicationConfig,
  Injector,
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
import { ProtectedCacheSafetyService } from './core/service-worker/protected-cache-safety.service';
import { APP_WORKER_SCRIPT } from './core/service-worker/service-worker-registration';
import { OfflineSalesQueueSyncService } from './features/sales/services/offline-sales-queue-sync.service';

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
      // Shop setup/management overlays use z-index 1200. Body-mounted dropdowns and
      // their tooltips must sit above those forms to remain visible and clickable.
      zIndex: { modal: 1100, overlay: 1300, menu: 1000, tooltip: 1400 },
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
    provideServiceWorker(APP_WORKER_SCRIPT, {
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

  return async () => {
    // In the background: no request waits on it, since every API request bypasses the worker.
    injector.get(ProtectedCacheSafetyService).start();
    // Also in the background, and loaded on demand so the first load does not carry it: writes this
    // device's offline pack flag, then checks for a new version now and on its own schedule.
    void import('./core/service-worker/offline-pack-update.service').then(
      ({ OfflinePackUpdateService }) => injector.get(OfflinePackUpdateService).start(),
      () => undefined,
    );
    await localizationService.initialize();
    await firstValueFrom(authService.bootstrapSession());

    const offlineSalesQueueSync = injector.get(OfflineSalesQueueSyncService);
    offlineSalesQueueSync.startAutoSyncWatchers();
    offlineSalesQueueSync.runStartupSync();
    await injector.get(ProductSignalRService).startConnection();
  };
}
