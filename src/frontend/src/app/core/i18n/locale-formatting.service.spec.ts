import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';

import { LocalizationService } from './localization.service';
import { SUPPORTED_LANGUAGES } from './language.constants';
import { LocaleFormattingService } from './locale-formatting.service';

describe('LocaleFormattingService', () => {
  const language = signal<string>('en-IN');
  let service: LocaleFormattingService;

  beforeEach(() => {
    language.set('en-IN');
    TestBed.configureTestingModule({
      providers: [{ provide: LocalizationService, useValue: { currentLanguage: language } }],
    });
    service = TestBed.inject(LocaleFormattingService);
  });

  it('formats finite INR amounts in every supported locale without changing the input', () => {
    for (const locale of SUPPORTED_LANGUAGES) {
      language.set(locale);
      for (const value of [0, -1.25, 123456.78]) {
        expect(service.formatMoney(value)).toBe(
          new Intl.NumberFormat(locale, {
            style: 'currency',
            currency: 'INR',
            minimumFractionDigits: 2,
            maximumFractionDigits: 2,
          }).format(value),
        );
      }
    }
  });

  it('uses an em dash for absent or invalid values', () => {
    expect(service.formatMoney(null)).toBe('—');
    expect(service.formatMoney(Number.NaN)).toBe('—');
    expect(service.formatBusinessDate(null)).toBe('—');
    expect(service.formatBusinessDate('2026-02-29')).toBe('—');
    expect(service.formatBusinessDate('2024-2-29')).toBe('—');
    expect(service.formatInstant('not-a-date')).toBe('—');
    expect(service.formatInstant('2024-02-29')).toBe('—');
    expect(service.formatInstant('2024-02-29T00:00:00Z', 'Mars/Phobos')).toBe('—');
  });

  it('preserves leap-day calendar fields regardless of the host time zone', () => {
    const oldZone = process.env['TZ'];
    try {
      for (const zone of ['UTC', 'Asia/Kolkata', 'America/Los_Angeles']) {
        process.env['TZ'] = zone;
        expect(service.formatBusinessDate('2024-02-29')).toBe(
          new Intl.DateTimeFormat('en-IN', {
            dateStyle: 'medium',
            timeZone: 'UTC',
          }).format(new Date(Date.UTC(2024, 1, 29))),
        );
      }
    } finally {
      if (oldZone === undefined) delete process.env['TZ'];
      else process.env['TZ'] = oldZone;
    }
  });

  it('formats instants in the requested zone separately from business dates', () => {
    const instant = '2024-02-28T20:00:00Z';
    expect(service.formatInstant(instant, 'Asia/Kolkata')).toBe(
      new Intl.DateTimeFormat('en-IN', {
        dateStyle: 'medium',
        timeStyle: 'short',
        timeZone: 'Asia/Kolkata',
      }).format(new Date(instant)),
    );
  });
});
