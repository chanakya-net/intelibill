import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';

import { LocalizationService } from '../../core/i18n/localization.service';
import { LocaleFormattingService } from '../../core/i18n/locale-formatting.service';
import { LocaleMoneyPipe } from './locale-money.pipe';
import { LocaleBusinessDatePipe } from './locale-business-date.pipe';
import { LocaleInstantPipe } from './locale-instant.pipe';

@Component({
  imports: [LocaleMoneyPipe, LocaleBusinessDatePipe, LocaleInstantPipe],
  template:
    '<span id="money">{{ 123456.78 | localeMoney }}</span><span id="day">{{ "2024-02-29" | localeBusinessDate }}</span><span id="instant">{{ "2024-02-28T20:00:00Z" | localeInstant }}</span>',
})
class HostComponent {}

describe('reactive locale display pipes', () => {
  it('updates displayed money and dates when the selected language changes', () => {
    const language = signal('en-IN');
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [
        { provide: LocalizationService, useValue: { currentLanguage: language } },
        LocaleFormattingService,
      ],
    });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const en = fixture.nativeElement.textContent;
    language.set('hi-IN');
    fixture.detectChanges();
    const hi = fixture.nativeElement.textContent;
    expect(hi).not.toBe(en);
    expect(fixture.nativeElement.querySelector('#money').textContent).toBe(
      new Intl.NumberFormat('hi-IN', {
        style: 'currency',
        currency: 'INR',
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      }).format(123456.78),
    );
    const date = new Intl.DateTimeFormat('hi-IN', {
      dateStyle: 'medium', timeZone: 'UTC',
    }).format(new Date('2024-02-29T00:00:00Z'));
    expect(fixture.nativeElement.querySelector('#day').textContent).toBe(date);
    const instant = new Intl.DateTimeFormat('hi-IN', {
      dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Kolkata',
    }).format(new Date('2024-02-28T20:00:00Z'));
    expect(fixture.nativeElement.querySelector('#instant').textContent).toBe(instant);
  });
});
