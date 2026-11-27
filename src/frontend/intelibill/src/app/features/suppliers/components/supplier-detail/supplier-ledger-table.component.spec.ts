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

  it('refreshes mobile ledger cards and totals when the parent changes the entries', () => {
    TestBed.configureTestingModule({
      imports: [
        SupplierLedgerTableComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [provideRouter([])],
    });
    const fixture = TestBed.createComponent(SupplierLedgerTableComponent);
    const goods = {
      id: 'goods',
      entryTypeLabel: 'Goods Received',
      entryTypeClass: 'goods-received',
      isPayment: false,
      displayAmount: 500,
      balance: 500,
      entryDate: '2026-11-09',
      notes: null,
    };
    const payment = {
      ...goods,
      id: 'payment',
      entryTypeLabel: 'Payment Made',
      isPayment: true,
      displayAmount: -100,
      balance: 400,
    };
    fixture.componentRef.setInput('entries', [goods, payment]);
    fixture.componentRef.setInput('loading', false);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.ledger-entry-card')).toHaveLength(2);
    expect(fixture.nativeElement.querySelector('.ledger-total-row strong').textContent).toContain(
      '400',
    );

    fixture.componentRef.setInput('entries', [goods]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.ledger-entry-card')).toHaveLength(1);
    expect(fixture.nativeElement.querySelector('.ledger-entry-card').textContent).toContain(
      'Goods Received',
    );
    expect(fixture.nativeElement.querySelector('.ledger-total-row strong').textContent).toContain(
      '500',
    );
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
