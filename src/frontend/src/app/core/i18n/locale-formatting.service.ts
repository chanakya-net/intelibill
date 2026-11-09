import { Injectable, inject } from '@angular/core';

import { LocalizationService } from './localization.service';

const MISSING = '—';
const DATE_ONLY = /^(\d{4})-(\d{2})-(\d{2})$/;
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/;

@Injectable({ providedIn: 'root' })
export class LocaleFormattingService {
  private readonly localization = inject(LocalizationService);

  formatMoney(value: number | null | undefined): string {
    if (typeof value !== 'number' || !Number.isFinite(value)) return MISSING;
    return new Intl.NumberFormat(this.localization.currentLanguage(), {
      style: 'currency',
      currency: 'INR',
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(value);
  }

  formatBusinessDate(value: string | null | undefined): string {
    const parts = typeof value === 'string' ? DATE_ONLY.exec(value) : null;
    if (!parts) return MISSING;
    const year = Number(parts[1]);
    const month = Number(parts[2]);
    const day = Number(parts[3]);
    const date = new Date(0);
    date.setUTCFullYear(year, month - 1, day);
    date.setUTCHours(0, 0, 0, 0);
    if (
      date.getUTCFullYear() !== year ||
      date.getUTCMonth() !== month - 1 ||
      date.getUTCDate() !== day
    ) {
      return MISSING;
    }
    return new Intl.DateTimeFormat(this.localization.currentLanguage(), {
      dateStyle: 'medium',
      timeZone: 'UTC',
    }).format(date);
  }

  formatInstant(value: string | null | undefined, timeZone = 'Asia/Kolkata'): string {
    if (
      !value ||
      typeof value !== 'string' ||
      !INSTANT.test(value) ||
      this.formatBusinessDate(value.slice(0, 10)) === MISSING
    )
      return MISSING;
    const date = new Date(value);
    if (!Number.isFinite(date.getTime())) return MISSING;
    try {
      return new Intl.DateTimeFormat(this.localization.currentLanguage(), {
        dateStyle: 'medium',
        timeStyle: 'short',
        timeZone,
      }).format(date);
    } catch (error) {
      if (error instanceof RangeError) return MISSING;
      throw error;
    }
  }
}
