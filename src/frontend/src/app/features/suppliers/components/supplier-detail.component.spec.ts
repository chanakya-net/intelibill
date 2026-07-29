import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService, TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { SupplierLedgerEntry } from '../services/supplier-ledger.service';
import { SuppliersFacade } from '../state/suppliers.facade';
import { SupplierDetailComponent } from './supplier-detail.component';

describe('SupplierDetailComponent', () => {
  const ledgerEntries: SupplierLedgerEntry[] = [
    {
      id: 'entry-1',
      supplierId: 'supplier-1',
      entryType: 'GOODS_RECEIVED',
      amount: 400,
      entryDate: '2026-04-06',
      notes: null,
    },
  ];

  const ledgerEntriesSignal = signal<readonly SupplierLedgerEntry[]>([]);
  const ledgerIsLoadingSignal = signal(false);

  const mockFacade = {
    ledgerEntries: ledgerEntriesSignal,
    ledgerIsLoading: ledgerIsLoadingSignal,
    ledgerErrorMessage: signal(''),
    isSubmitting: signal(false),
    errorMessage: signal(''),
    lastMutationType: signal<'add-supplier' | 'edit-supplier' | 'make-payment' | null>(null),
    lastMutationSucceeded: signal(false),
    loadLedger: vi.fn((supplierId: string) => {
      void supplierId;
      ledgerEntriesSignal.set(ledgerEntries);
    }),
    clearLedger: vi.fn(),
    clearError: vi.fn(),
    clearMutationStatus: vi.fn(),
    makePayment: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    ledgerEntriesSignal.set([]);
    ledgerIsLoadingSignal.set(false);

    TestBed.configureTestingModule({
      imports: [
        SupplierDetailComponent,
        TranslocoTestingModule.forRoot({
          preloadLangs: true,
          translocoConfig: {
            availableLangs: ['en-IN', 'hi-IN'],
            defaultLang: 'en-IN',
            reRenderOnLangChange: true,
          },
          langs: {
            'en-IN': {
              common: {
                clear: 'Clear',
              },
              suppliers: {
                ledgerEntries: 'Ledger Entries',
                name: 'Name',
                contactPerson: 'Contact Person',
                contactPhone: 'Contact Phone',
                entryType: 'Entry Type',
                amount: 'Amount',
                entryDate: 'Entry Date',
                notes: 'Notes',
                noEntriesFound: 'No ledger entries found',
                searchLedger: 'Search ledger',
                totalAmount: 'Total Amount',
                details: 'Details',
                entryTypes: {
                  goodsReceived: 'Goods Received',
                  paymentMade: 'Payment Made',
                  recordAdjusted: 'Record Adjusted',
                },
              },
            },
            'hi-IN': {
              common: {
                clear: 'साफ़ करें',
              },
              suppliers: {
                ledgerEntries: 'लेजर प्रविष्टियाँ',
                name: 'नाम',
                contactPerson: 'संपर्क व्यक्ति',
                contactPhone: 'संपर्क फोन',
                entryType: 'प्रविष्टि प्रकार',
                amount: 'राशि',
                entryDate: 'प्रविष्टि तिथि',
                notes: 'टिप्पणियाँ',
                noEntriesFound: 'कोई लेजर प्रविष्टि नहीं मिली',
                searchLedger: 'खाता खोजें',
                totalAmount: 'कुल राशि',
                details: 'विवरण',
                entryTypes: {
                  goodsReceived: 'माल प्राप्त',
                  paymentMade: 'भुगतान किया गया',
                  recordAdjusted: 'रिकॉर्ड समायोजित',
                },
              },
            },
          },
        }),
      ],
      providers: [{ provide: SuppliersFacade, useValue: mockFacade }],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('updates entry type labels when language changes without re-fetching entries', () => {
    const transloco = TestBed.inject(TranslocoService);
    transloco.setActiveLang('en-IN');

    const fixture = TestBed.createComponent(SupplierDetailComponent);
    const component = fixture.componentInstance;

    component.supplierId = 'supplier-1';
    fixture.detectChanges();

    expect((component as any).tableEntries()[0].entryTypeLabel).toBe('Goods Received');
    expect(mockFacade.loadLedger).toHaveBeenCalledTimes(1);

    transloco.setActiveLang('hi-IN');
    fixture.detectChanges();

    expect((component as any).tableEntries()[0].entryTypeLabel).toBe('माल प्राप्त');
    expect(mockFacade.loadLedger).toHaveBeenCalledTimes(1);
  });
});
