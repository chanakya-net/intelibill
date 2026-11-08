import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { describe, it, expect, afterEach } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import { TillClosePrintPageComponent } from './till-close-print-page.component';
import { TillService } from '../services/till.service';
describe('Frozen close print', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('renders the saved close boundary/cashier/date/totals, never current session totals, and separates unknown supplements', () => {
    const session = {
      id: 'd',
      tillName: 'Renamed current till',
      cashierName: 'Current name',
      expectedCash: 99999,
      recordedTenderTotals: { Cash: 99999 },
      awaitingReview: false,
      review: null,
      close: {
        id: 'close-ref',
        tillName: 'Frozen front counter',
        cashierName: 'Frozen cashier',
        cashierId: 'cashier-ref',
        businessDate: '2026-11-07',
        closedAt: '2026-11-08T08:00:00Z',
        closedBy: 'manager',
        isOverdue: true,
        openingFloat: 7,
        expectedCash: 12,
        countedCash: 10,
        variance: -2,
        lastMovementSequence: 42,
        totals: {
          movementCount: 3,
          recordedTenderTotals: { Cash: 5, Upi: 30 },
          purposeTotals: { sale: 35 },
          storeCreditIssued: 0,
          storeCreditRedeemed: 0,
          receivableChange: 4,
        },
        reason: 'Overdue count witnessed',
      },
      latePostings: [
        {
          id: 'late',
          sourceBusinessDate: '2026-11-07',
          recordedAt: '2026-11-09T08:00:00Z',
          evidenceStatus: 'Unknown',
          supplementalAmount: null,
          includedInCount: false,
          managerReason: 'Reviewed legacy envelope',
          saleId: 'sale',
          caseId: 'case',
        },
      ],
    };
    TestBed.configureTestingModule({
      imports: [
        TillClosePrintPageComponent,
        TranslocoTestingModule.forRoot({
          langs: {
            'en-IN': { ...en, shops: { ...en.shops, unknown: 'Translated unknown evidence' } },
          },
          translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' },
        }),
      ],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: 'd' }) } },
        },
        { provide: TillService, useValue: { detail: () => of({ session }) } },
      ],
    });
    const fixture = TestBed.createComponent(TillClosePrintPageComponent);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent!;
    expect(text).toContain('Frozen front counter');
    expect(text).toContain('Frozen cashier');
    expect(text).toContain('2026-11-07');
    expect(text).toContain('#42');
    expect(text).toContain('12.00');
    expect(text).toContain('10.00');
    expect(text).toContain('Amount unknown');
    expect(text).toContain('Sale:');
    expect(text).not.toContain('sale:');
    expect(text).toContain('Translated unknown evidence');
    expect(text).not.toContain(' · Unknown');
    expect(text).toContain('excluded from the original count');
    expect(text).not.toContain('99,999');
    expect(text).not.toContain('Renamed current till');
  });
});
