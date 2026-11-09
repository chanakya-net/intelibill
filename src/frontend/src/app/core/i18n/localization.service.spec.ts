import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { TranslocoService } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthStorage } from '../auth/auth.storage';
import { LocalizationService } from './localization.service';

describe('LocalizationService document language', () => {
  let load: ReturnType<typeof vi.fn>;
  let storage: { getLanguage: ReturnType<typeof vi.fn>; saveLanguage: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    document.documentElement.lang = 'en-IN';
    load = vi.fn(() => of({}));
    storage = { getLanguage: vi.fn(() => 'en-IN'), saveLanguage: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        {
          provide: TranslocoService,
          useValue: { load, setActiveLang: vi.fn(), translate: vi.fn() },
        },
        { provide: AuthStorage, useValue: storage },
        { provide: Store, useValue: { dispatch: vi.fn() } },
      ],
    });
  });

  it('updates document lang only after a supported language loads', async () => {
    const service = TestBed.inject(LocalizationService);
    await service.setLanguage('hi-IN');
    expect(document.documentElement.lang).toBe('hi-IN');
    expect(storage.saveLanguage).toHaveBeenCalledWith('hi-IN');
  });

  it('keeps the prior language and preference after failed loading', async () => {
    const service = TestBed.inject(LocalizationService);
    await service.setLanguage('hi-IN');
    load.mockReturnValue(throwError(() => new Error('network')));
    await expect(service.setLanguage('ta-IN')).rejects.toThrow('network');
    expect(document.documentElement.lang).toBe('hi-IN');
    expect(service.currentLanguage()).toBe('hi-IN');
    expect(storage.saveLanguage).toHaveBeenCalledTimes(1);
  });
});
