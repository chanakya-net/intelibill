import { of } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { TranslocoService, TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { SupplierLedgerService } from '../services/supplier-ledger.service';
import { SupplierDetailComponent } from './supplier-detail.component';

describe('SupplierDetailComponent', () => {
  const ledgerEntries = [
    {
      id: 'entry-1',
      supplierId: 'supplier-1',
      entryType: 'GOODS_RECEIVED' as const,
      amount: 400,
      entryDate: '2026-04-06',
      notes: null,
    },
  ];

  const supplierLedgerService = {
    getSupplierLedgerEntries: vi.fn<SupplierLedgerService['getSupplierLedgerEntries']>(),
  };

  beforeEach(() => {
    supplierLedgerService.getSupplierLedgerEntries.mockReset();
    supplierLedgerService.getSupplierLedgerEntries.mockReturnValue(of(ledgerEntries));

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
                searchEntryType: 'Search entry type',
                searchAmount: 'Search amount',
                searchEntryDate: 'Search date',
                searchNotes: 'Search notes',
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
                searchEntryType: 'प्रविष्टि प्रकार खोजें',
                searchAmount: 'राशि खोजें',
                searchEntryDate: 'तिथि खोजें',
                searchNotes: 'टिप्पणियाँ खोजें',
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
      providers: [{ provide: SupplierLedgerService, useValue: supplierLedgerService }],
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
    expect(supplierLedgerService.getSupplierLedgerEntries).toHaveBeenCalledTimes(1);

    transloco.setActiveLang('hi-IN');
    fixture.detectChanges();

    expect((component as any).tableEntries()[0].entryTypeLabel).toBe('माल प्राप्त');
    expect(supplierLedgerService.getSupplierLedgerEntries).toHaveBeenCalledTimes(1);
  });
});
