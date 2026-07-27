import { Injectable, inject, signal } from '@angular/core';
import { Store } from '@ngrx/store';

import { firstValueFrom } from 'rxjs';
import { TranslocoService } from '@ngneat/transloco';

import { AuthStorage } from '../auth/auth.storage';
import { AppShellActions } from '../state/app-shell.actions';
import { RootState } from '../state/app.state';
import { DEFAULT_LANGUAGE, SUPPORTED_LANGUAGES } from './language.constants';

@Injectable({ providedIn: 'root' })
export class LocalizationService {
  private readonly transloco = inject(TranslocoService);
  private readonly storage = inject(AuthStorage);
  private readonly store = inject(Store<RootState>);
  private readonly currentLanguageSignal = signal<string>(DEFAULT_LANGUAGE);

  readonly currentLanguage = this.currentLanguageSignal.asReadonly();
  readonly supportedLanguages = SUPPORTED_LANGUAGES;

  async initialize(): Promise<void> {
    const initialLanguage = this.getInitialLanguage();
    await this.setLanguage(initialLanguage, false);
  }

  async setLanguage(language: string, persist = true): Promise<void> {
    const normalizedLanguage = this.normalizeLanguage(language);

    await firstValueFrom(this.transloco.load(normalizedLanguage));
    this.transloco.setActiveLang(normalizedLanguage);
    this.currentLanguageSignal.set(normalizedLanguage);
    this.store.dispatch(AppShellActions.setLanguage({ language: normalizedLanguage }));

    if (persist) {
      this.storage.saveLanguage(normalizedLanguage);
    }
  }

  translate(key: string): string {
    return this.transloco.translate(key);
  }

  private getInitialLanguage(): string {
    return this.normalizeLanguage(this.storage.getLanguage());
  }

  private normalizeLanguage(language: string | null | undefined): string {
    if (!language) {
      return DEFAULT_LANGUAGE;
    }

    return SUPPORTED_LANGUAGES.includes(language as (typeof SUPPORTED_LANGUAGES)[number])
      ? language
      : DEFAULT_LANGUAGE;
  }

}
