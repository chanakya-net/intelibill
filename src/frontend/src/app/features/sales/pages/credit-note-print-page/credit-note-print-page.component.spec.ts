import { CommonModule } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../../core/auth/auth.service';
import { ShopService } from '../../../shops/services/shop.service';
import { CreditNotePrintDto, SaleService } from '../../services/sale.service';
import { CreditNotePrintPageComponent } from './credit-note-print-page.component';

const enIN = JSON.parse(readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8') as string) as Record<string, unknown>;

describe('CreditNotePrintPageComponent', () => {
  const shop = {
    shopId: 'shop-1',
    name: 'Main Shop',
    address: '1 Market Road',
    city: 'Mumbai',
    state: 'MH',
    pincode: '400001',
    contactPerson: 'Owner',
    mobileNumber: '9000000000',
    gstNumber: 'GSTIN',
    bankName: null,
    bankAccountNumber: null,
    bankAccountType: null,
    ifscCode: null,
    accountHolderName: null,
  };

  const creditNote: CreditNotePrintDto = {
    creditNoteId: 'cn-1',
    code: 'CN-ABC-123',
    status: 'Active',
    isUsable: true,
    originalAmount: 250,
    availableBalance: 180,
    issuedAt: '2026-05-02T10:00:00Z',
    expiresAt: '2026-06-02T00:00:00Z',
    saleId: 'sale-1',
    invoiceNumber: 'INV-001',
    saleReturnId: 'return-1',
    returnNumber: 'RET-001',
    customerDisplayName: 'Walk-in Customer',
    reason: 'Damaged item return',
    voidReason: null,
  };

  const saleService = {
    getCreditNotePrintByCode: vi.fn(),
  };

  const shopService = {
    getShopDetails: vi.fn(),
  };

  const authService = {
    session: vi.fn(),
  };

  const createActivatedRoute = (code?: string) => ({
    snapshot: {
      paramMap: convertToParamMap(code ? { code } : {}),
    },
  });

  const createComponent = (code?: string): ComponentFixture<CreditNotePrintPageComponent> => {
    TestBed.configureTestingModule({
      imports: [
        CommonModule,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
        CreditNotePrintPageComponent,
      ],
      providers: [
        { provide: ActivatedRoute, useValue: createActivatedRoute(code) },
        { provide: SaleService, useValue: saleService },
        { provide: ShopService, useValue: shopService },
        { provide: AuthService, useValue: authService },
      ],
    });

    return TestBed.createComponent(CreditNotePrintPageComponent);
  };

  beforeEach(() => {
    vi.useFakeTimers();
    vi.spyOn(window, 'print').mockImplementation(() => undefined);
    saleService.getCreditNotePrintByCode.mockReset();
    shopService.getShopDetails.mockReset();
    authService.session.mockReset();
    saleService.getCreditNotePrintByCode.mockReturnValue(of(creditNote));
    shopService.getShopDetails.mockReturnValue(of(shop));
    authService.session.mockReturnValue({
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main Shop', role: 'Owner', isDefault: true, lastUsedAt: null }],
    });
  });

  afterEach(() => {
    vi.runOnlyPendingTimers();
    vi.useRealTimers();
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('loads credit note and active shop details before printing', () => {
    const fixture = createComponent('CN-ABC-123');

    expect(saleService.getCreditNotePrintByCode).toHaveBeenCalledWith('CN-ABC-123');
    expect(shopService.getShopDetails).toHaveBeenCalledWith('shop-1');
    expect(window.print).not.toHaveBeenCalled();

    fixture.detectChanges();
    vi.runOnlyPendingTimers();

    expect(fixture.componentInstance.creditNote()).toEqual(creditNote);
    expect(fixture.componentInstance.shop()).toEqual(shop);
    expect(window.print).toHaveBeenCalledTimes(1);
  });

  it('renders required credit note fields and terms', () => {
    const fixture = createComponent('CN-ABC-123');

    fixture.detectChanges();
    vi.runOnlyPendingTimers();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Main Shop');
    expect(host.textContent).toContain('Credit Note');
    expect(host.textContent).toContain('CN-ABC-123');
    expect(host.textContent).toContain('02 May 2026');
    expect(host.textContent).toContain('02 Jun 2026');
    expect(host.textContent).toContain('INV-001');
    expect(host.textContent).toContain('RET-001');
    expect(host.textContent).toContain('Walk-in Customer');
    expect(host.textContent).toContain('250.00');
    expect(host.textContent).toContain('180.00');
    expect(host.textContent).toContain('Credit notes are subject to store policy');
    expect(window.print).toHaveBeenCalledTimes(1);
  });

  it('marks expired credit notes visibly', () => {
    saleService.getCreditNotePrintByCode.mockReturnValue(
      of({
        ...creditNote,
        status: 'Expired',
        isUsable: false,
      }),
    );

    const fixture = createComponent('CN-EXPIRED-1');
    fixture.detectChanges();
    vi.runOnlyPendingTimers();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Expired');
    expect(host.textContent).toContain('Expires on');
    expect(host.textContent).toContain('Credit note is expired');
    expect(window.print).toHaveBeenCalledTimes(1);
  });

  it('marks voided credit notes visibly and shows the void reason', () => {
    saleService.getCreditNotePrintByCode.mockReturnValue(
      of({
        ...creditNote,
        status: 'Voided',
        isUsable: false,
        voidReason: 'Customer request',
      }),
    );

    const fixture = createComponent('CN-VOIDED-1');
    fixture.detectChanges();
    vi.runOnlyPendingTimers();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Voided');
    expect(host.textContent).toContain('Void reason');
    expect(host.textContent).toContain('Customer request');
    expect(window.print).toHaveBeenCalledTimes(1);
  });

  it('shows an error and does not print when the credit note code is missing', () => {
    const fixture = createComponent();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Credit note was not found.');
    expect(saleService.getCreditNotePrintByCode).not.toHaveBeenCalled();
    expect(window.print).not.toHaveBeenCalled();
  });

  it('shows an error when loading fails', () => {
    saleService.getCreditNotePrintByCode.mockReturnValue(
      throwError(() => ({ error: { detail: 'Unable to print credit note.' } })),
    );

    const fixture = createComponent('CN-FAIL-1');
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Unable to print credit note.');
    expect(window.print).not.toHaveBeenCalled();
  });

  it('prints again when requested', () => {
    const fixture = createComponent('CN-ABC-123');
    fixture.detectChanges();
    vi.runOnlyPendingTimers();
    vi.mocked(window.print).mockClear();

    fixture.componentInstance.onPrintAgain();

    expect(window.print).toHaveBeenCalledTimes(1);
  });
});
