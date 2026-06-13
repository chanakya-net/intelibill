import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { of } from 'rxjs';

import { SaleService } from '../../services/sale.service';
import type {
  CreditNoteDetailDto,
  CreditNoteListItemDto,
  CreditNotesResultDto,
} from '../../services/sale.models';
import { CreditNotesPageComponent } from './credit-notes-page.component';

describe('CreditNotesPageComponent', () => {
  const notesResponse = (items: readonly CreditNoteListItemDto[]): CreditNotesResultDto => ({
    items,
    totalCount: items.length,
    pageNumber: 1,
    pageSize: 20,
  });

  const note: CreditNoteListItemDto = {
    creditNoteId: 'cn-1',
    code: 'CN-001',
    status: 'Active',
    originalAmount: 500,
    availableBalance: 300,
    expiresAt: '2027-01-01T00:00:00Z',
    issuedAt: '2026-05-20T10:00:00.000Z',
    saleReturnId: 'return-1',
    returnNumber: 'RET-001',
    saleId: 'sale-1',
    invoiceNumber: 'INV-001',
    customerName: 'Asha',
  };

  const detail: CreditNoteDetailDto = {
    creditNoteId: 'cn-1',
    code: 'CN-001',
    status: 'Active',
    originalAmount: 500,
    availableBalance: 300,
    expiresAt: '2027-01-01T00:00:00Z',
    isVoided: false,
    saleReturnId: 'return-1',
    reason: 'Return adjustment',
    voidReason: null,
    redemptions: [
      {
        saleId: 'sale-2',
        invoiceNumber: 'INV-002',
        amount: 200,
        redeemedAt: '2026-05-25T12:00:00.000Z',
      },
    ],
  };

  const saleService = {
    getCreditNotes: vi.fn(),
    verifyCreditNote: vi.fn(),
  };

  const translations = {
    sales: {
      history: {
        walkIn: 'Walk-in',
      },
      creditNotes: {
        eyebrow: 'Sales Records',
        title: 'Credit Notes',
        subtitle: 'Review credit note history for the active shop.',
        verify: {
          label: 'Verify code',
          placeholder: 'Enter credit note code',
          action: 'Verify',
        },
        controls: {
          search: 'Search',
          searchPlaceholder: 'Search by code, invoice, return number, or customer',
          status: 'Status',
          pageSize: 'Rows',
          clear: 'Clear filters',
        },
        filters: {
          all: 'All',
          active: 'Active',
          fullyRedeemed: 'Fully redeemed',
          expired: 'Expired',
          voided: 'Voided',
        },
        list: {
          title: 'Credit note list',
          subtitle: 'Browse codes and balances.',
          count: '{{total}} notes',
        },
        table: {
          code: 'Code',
          status: 'Status',
          balance: 'Balance',
          originalAmount: 'Original amount',
          customer: 'Customer',
          invoice: 'Invoice',
          returnNumber: 'Return number',
          issuedAt: 'Issued at',
        },
        status: {
          Active: 'Active',
          Voided: 'Voided',
          FullyRedeemed: 'Fully redeemed',
          Expired: 'Expired',
        },
        empty: {
          title: 'No credit notes found',
          description: 'Use search or status filters to narrow the list.',
        },
        loading: 'Loading credit notes...',
        pagination: {
          showing: 'Showing {{total}} notes, page {{page}} of {{pages}}',
        },
        detail: {
          eyebrow: 'Selected credit note',
          subtitle: 'Review balance, customer context, and redemption activity.',
          balance: 'Balance',
          originalAmount: 'Original amount',
          expiresAt: 'Expires at',
          customer: 'Customer',
          invoice: 'Invoice',
          returnNumber: 'Return number',
          reason: 'Reason',
          voidReason: 'Void reason',
        },
        redemptions: {
          title: 'Redemption history',
          subtitle: 'Applications against this credit note.',
          empty: 'No redemption history available.',
          table: {
            saleId: 'Sale',
            invoice: 'Invoice',
            amount: 'Amount',
            redeemedAt: 'Redeemed at',
          },
        },
        errors: {
          loadFailed: 'Unable to load credit notes.',
          verifyFailed: 'Unable to verify credit note.',
        },
      },
    },
  };

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-05-30T10:00:00.000Z'));

    saleService.getCreditNotes.mockImplementation((params?: Record<string, unknown>) => {
      if (params?.['search'] === 'CN-001') {
        return of(notesResponse([note]));
      }

      return of(notesResponse([note]));
    });
    saleService.verifyCreditNote.mockReturnValue(of(detail));

    TestBed.configureTestingModule({
      imports: [
        CreditNotesPageComponent,
        TranslocoTestingModule.forRoot({
          preloadLangs: true,
          translocoConfig: {
            availableLangs: ['en-IN'],
            defaultLang: 'en-IN',
            reRenderOnLangChange: true,
          },
          langs: {
            'en-IN': translations,
          },
        }),
      ],
      providers: [{ provide: SaleService, useValue: saleService }],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  it('loads the credit note list on init', async () => {
    const fixture = TestBed.createComponent(CreditNotesPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(saleService.getCreditNotes).toHaveBeenCalledWith({
      search: undefined,
      status: undefined,
      page: 1,
      pageSize: 20,
    });
    expect(fixture.nativeElement.textContent).toContain('CN-001');
    expect(fixture.nativeElement.textContent).toContain('₹300.00');
  });

  it('debounces search and reloads the list', async () => {
    const fixture = TestBed.createComponent(CreditNotesPageComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    saleService.getCreditNotes.mockClear();

    component.onSearchChange('CN-001');
    vi.advanceTimersByTime(299);
    expect(saleService.getCreditNotes).not.toHaveBeenCalled();

    vi.advanceTimersByTime(2);
    expect(saleService.getCreditNotes).toHaveBeenCalledWith({
      search: 'CN-001',
      status: undefined,
      page: 1,
      pageSize: 20,
    });
  });

  it('verifies a code and renders detail panel fields', async () => {
    const fixture = TestBed.createComponent(CreditNotesPageComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    await Promise.resolve();

    saleService.getCreditNotes.mockClear();
    saleService.verifyCreditNote.mockClear();

    component.verifyCode.set('CN-001');
    await component.onVerifyCode();
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    expect(saleService.verifyCreditNote).toHaveBeenCalledWith('CN-001');
    expect(saleService.getCreditNotes).toHaveBeenCalledWith({
      search: 'CN-001',
      page: 1,
      pageSize: 20,
    });

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Balance');
    expect(text).toContain('₹300.00');
    expect(text).toContain('Original amount');
    expect(text).toContain('₹500.00');
    expect(text).toContain('Asha');
    expect(text).toContain('INV-001');
    expect(text).toContain('RET-001');
    expect(text).toContain('Redemption history');
    expect(text).toContain('INV-002');
  });

  it('updates selection from a list item action', async () => {
    const fixture = TestBed.createComponent(CreditNotesPageComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    await component.openCreditNoteFromList(note);
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    expect(saleService.verifyCreditNote).toHaveBeenCalledWith('CN-001');
    expect(fixture.nativeElement.textContent).toContain('Selected credit note');
    expect(fixture.nativeElement.textContent).toContain('Redemption history');
  });
});
