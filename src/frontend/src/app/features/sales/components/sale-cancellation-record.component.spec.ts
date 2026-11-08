import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';

import { cancellationFixture } from '../services/sale-cancellation.test-fixture';
import { SaleCancellationRecordComponent } from './sale-cancellation-record.component';

describe('SaleCancellationRecordComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        SaleCancellationRecordComponent,
        TranslocoTestingModule.forRoot({
          langs: { en: {} },
          translocoConfig: { defaultLang: 'en', availableLangs: ['en'] },
          preloadLangs: true,
        }),
      ],
    }).compileComponents();
  });

  it.each([
    ['Asia/Kolkata', '2026-11-10', 'Nov 10, 2026, 1:30 AM'],
    ['America/New_York', '2026-11-09', 'Nov 09, 2026, 3:00 PM'],
  ])('renders the cancellation instant in its persisted %s timezone', (timeZone, businessDate, timestamp) => {
    const fixture = TestBed.createComponent(SaleCancellationRecordComponent);
    fixture.componentRef.setInput('cancellation', {
      ...cancellationFixture,
      cancelledAt: '2026-11-09T20:00:00Z',
      businessDate,
      businessTimeZoneId: timeZone,
    });
    fixture.detectChanges();

    const text = (fixture.nativeElement.querySelector('.sale-cancellation-record')?.textContent ?? '')
      .replace(/\s+/g, ' ');
    expect(text).toContain(`${businessDate} · ${timestamp} (${timeZone})`);
    expect(text).toContain('A/2627/000042 · 2026-11-08');
  });
});
