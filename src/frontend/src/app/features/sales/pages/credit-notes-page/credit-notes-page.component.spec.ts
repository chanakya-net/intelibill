import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { of, throwError } from 'rxjs';

import { AuthService } from '../../../../core/auth/auth.service';
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
    invoiceNumber: 'INV-001',
    returnNumber: 'RET-001',
    customerName: 'Asha',
    voidReason: null,
  };

  const fuzzyDetail: CreditNoteDetailDto = {
    creditNoteId: 'cn-2',
    code: 'CN-999',
    status: 'FullyRedeemed',
    originalAmount: 750,
    availableBalance: 0,
    expiresAt: '2027-03-01T00:00:00Z',
    isVoided: false,
    saleReturnId: 'return-2',
    reason: 'Adjustment',
    invoiceNumber: 'INV-009',
    returnNumber: 'RET-009',
    customerName: 'Ravi',
    voidReason: null,
  };

  const fuzzyNote: CreditNoteListItemDto = {
    creditNoteId: 'cn-2',
    code: 'CN-009',
    status: 'FullyRedeemed',
    originalAmount: 750,
    availableBalance: 0,
    expiresAt: '2027-03-01T00:00:00Z',
    issuedAt: '2026-05-21T10:00:00.000Z',
    saleReturnId: 'return-2',
    returnNumber: 'RET-009',
    saleId: 'sale-9',
    invoiceNumber: 'INV-009',
    customerName: 'Ravi',
  };

  const saleService = {
    getCreditNotes: vi.fn(),
    verifyCreditNote: vi.fn(),
    voidCreditNote: vi.fn(),
  };

  const authService = {
    session: signal({
      accessToken: 'access-token',
      refreshToken: 'refresh-token',
      accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
      refreshTokenExpiresAt: new Date(Date.now() + 120_000).toISOString(),
      rememberMe: true,
      user: {
        id: 'owner-1',
        email: 'owner@test.com',
        phoneNumber: null,
        firstName: 'Owner',
        lastName: 'One',
      },
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
    }),
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
          subtitle: 'Review balance and customer context.',
          balance: 'Balance',
          originalAmount: 'Original amount',
          expiresAt: 'Expires at',
          customer: 'Customer',
          invoice: 'Invoice',
          returnNumber: 'Return number',
          reason: 'Reason',
          voidReason: 'Void reason',
        },
        void: {
          action: 'Void credit note',
          title: 'Void credit note',
          description: 'This will void the credit note and keep the audit trail intact.',
          reasonLabel: 'Void reason',
          reasonPlaceholder: 'Explain why this credit note is being voided',
          cancel: 'Cancel',
          confirm: 'Void credit note',
          errors: {
            reasonRequired: 'Void reason is required.',
            submitFailed: 'Unable to void credit note.',
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
    authService.session.set({
      ...authService.session(),
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
    });

    saleService.getCreditNotes.mockReset();
    saleService.verifyCreditNote.mockReset();
    saleService.voidCreditNote.mockReset();
    saleService.getCreditNotes.mockImplementation((params?: Record<string, unknown>) => {
      if (params?.['search'] === 'CN-001') {
        return of(notesResponse([note]));
      }

      if (params?.['search'] === 'CN-999') {
        return of(notesResponse([fuzzyNote]));
      }

      return of(notesResponse([note]));
    });
    saleService.verifyCreditNote.mockImplementation((code: string) =>
      of(code === 'CN-999' ? fuzzyDetail : detail),
    );

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
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: SaleService, useValue: saleService },
      ],
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
    expect(saleService.getCreditNotes).not.toHaveBeenCalled();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Balance');
    expect(text).toContain('₹300.00');
    expect(text).toContain('Original amount');
    expect(text).toContain('₹500.00');
    expect(text).toContain('Asha');
    expect(text).toContain('INV-001');
    expect(text).toContain('RET-001');
    expect(text).not.toContain('Redemption history');
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
    expect(fixture.nativeElement.textContent).toContain('Asha');
  });

  it('shows void action only for owner and manager users', async () => {
    const fixture = TestBed.createComponent(CreditNotesPageComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    await component.openCreditNoteFromList(note);
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.detail-actions button')).toBeTruthy();

    authService.session.set({
      ...authService.session(),
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Manager', isDefault: true, lastUsedAt: null }],
    });
    fixture.detectChanges();

    expect(host.querySelector('.detail-actions button')).toBeTruthy();

    authService.session.set({
      ...authService.session(),
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Staff', isDefault: true, lastUsedAt: null }],
    });
    fixture.detectChanges();

    expect(host.querySelector('.detail-actions button')).toBeNull();
  });

  it('requires a void reason before submission', async () => {
    const fixture = TestBed.createComponent(CreditNotesPageComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    await Promise.resolve();

    saleService.voidCreditNote.mockClear();
    await component.openCreditNoteFromList(note);
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    component.openVoidDialog();
    fixture.detectChanges();

    await component.submitVoidCreditNote();
    fixture.detectChanges();

    expect(saleService.voidCreditNote).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Void reason is required.');
  });

  it('voids the selected credit note and refreshes the list', async () => {
    const fixture = TestBed.createComponent(CreditNotesPageComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    await Promise.resolve();

    saleService.getCreditNotes.mockClear();
    saleService.voidCreditNote.mockReturnValue(of(void 0));

    await component.openCreditNoteFromList(note);
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    component.openVoidDialog();
    component.updateVoidReason('Issued in error');
    fixture.detectChanges();

    await component.submitVoidCreditNote();
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    expect(saleService.voidCreditNote).toHaveBeenCalledWith('CN-001', { reason: 'Issued in error' });
    expect(saleService.getCreditNotes).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.textContent).toContain('Voided');
    expect(fixture.nativeElement.textContent).toContain('Void reason');
  });

  it('keeps the void dialog open when submission fails', async () => {
    const fixture = TestBed.createComponent(CreditNotesPageComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    await Promise.resolve();

    saleService.voidCreditNote.mockReturnValue(throwError(() => new Error('server error')));

    await component.openCreditNoteFromList(note);
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    component.openVoidDialog();
    component.updateVoidReason('Issued in error');
    fixture.detectChanges();

    await component.submitVoidCreditNote();
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    expect(saleService.voidCreditNote).toHaveBeenCalledWith('CN-001', { reason: 'Issued in error' });
    expect(component.voidDialogVisible()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Unable to void credit note.');
  });

  it('renders required fields from exact verify response when search index is incomplete', async () => {
    const fixture = TestBed.createComponent(CreditNotesPageComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    await Promise.resolve();

    saleService.getCreditNotes.mockClear();
    saleService.verifyCreditNote.mockClear();

    component.verifyCode.set('CN-999');
    await component.onVerifyCode();
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    expect(saleService.verifyCreditNote).toHaveBeenCalledWith('CN-999');
    expect(saleService.getCreditNotes).not.toHaveBeenCalled();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('CN-999');
    expect(text).toContain('₹0.00');
    expect(text).toContain('Balance');
    expect(text).toContain('Original amount');
    expect(text).toContain('₹750.00');
    expect(text).toContain('Ravi');
    expect(text).toContain('INV-009');
    expect(text).toContain('RET-009');
  });
});
