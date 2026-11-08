import { provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, describe, expect, it } from 'vitest';

import { SupplierLedgerTableComponent } from './supplier-ledger-table.component';

describe('SupplierLedgerTableComponent', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('formats signed amounts', () => {
    TestBed.configureTestingModule({
      imports: [
        SupplierLedgerTableComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(SupplierLedgerTableComponent);
    const component = fixture.componentInstance;

    expect(component.formatSignedAmount(100)).toMatch(/^\+/);
    expect(component.formatSignedAmount(-100)).toMatch(/^\-/);
  });
  it('opens the real receipt behind a supplier payment, including historical receipts', () => {
    TestBed.configureTestingModule({
      imports: [
        SupplierLedgerTableComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [provideRouter([])],
    });
    const fixture = TestBed.createComponent(SupplierLedgerTableComponent);
    fixture.componentRef.setInput('entries', [
      {
        id: 'ledger-row',
        entryTypeLabel: 'Payment',
        entryTypeClass: 'payment',
        isPayment: true,
        displayAmount: -100,
        balance: 0,
        entryDate: '2026-11-08',
        notes: null,
        receiptId: 'actual-receipt',
      },
    ]);
    fixture.componentRef.setInput('loading', false);
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('a[href="/payments/actual-receipt"]'),
    ).not.toBeNull();
  });
});
