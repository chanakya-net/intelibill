import { CommonModule } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { AuthService } from '../../../../core/auth/auth.service';
import { ShopService } from '../../../shops/services/shop.service';
import { SaleDto, SaleService } from '../../services/sale.service';
import { SaleInvoicePrintPageComponent } from './sale-invoice-print-page.component';

describe('SaleInvoicePrintPageComponent', () => {
  const sale: SaleDto = {
    saleId: 'sale-1',
    invoiceNumber: 'INV-001',
    customerId: null,
    customerName: 'Walk-in',
    customerPhone: null,
    paymentMethod: 1,
    soldAt: '2026-05-01T10:00:00Z',
    paidAmount: 100,
    dueAmount: 0,
    totalBeforeDiscount: 100,
    totalDiscountAmount: 0,
    totalAmount: 100,
    totalTaxAmount: 5,
    items: [],
    returns: [],
    warnings: [],
  };

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

  const saleService = {
    getSaleById: vi.fn(),
  };

  const shopService = {
    getShopDetails: vi.fn(),
  };

  const authService = {
    session: vi.fn(),
  };

  const createActivatedRoute = (template?: string, saleId = 'sale-1') => ({
    snapshot: {
      paramMap: convertToParamMap({ saleId }),
      queryParamMap: convertToParamMap(template === undefined ? {} : { template }),
    },
  });

  const createComponent = (template?: string): ComponentFixture<SaleInvoicePrintPageComponent> => {
    TestBed.configureTestingModule({
      imports: [CommonModule, SaleInvoicePrintPageComponent],
      providers: [
        { provide: ActivatedRoute, useValue: createActivatedRoute(template) },
        { provide: SaleService, useValue: saleService },
        { provide: ShopService, useValue: shopService },
        { provide: AuthService, useValue: authService },
      ],
    });

    return TestBed.createComponent(SaleInvoicePrintPageComponent);
  };

  beforeEach(() => {
    vi.useFakeTimers();
    vi.spyOn(window, 'print').mockImplementation(() => undefined);
    saleService.getSaleById.mockReset();
    shopService.getShopDetails.mockReset();
    authService.session.mockReset();
    saleService.getSaleById.mockReturnValue(of(sale));
    shopService.getShopDetails.mockReturnValue(of(shop));
    authService.session.mockReturnValue({
      activeShopId: 'shop-1',
    });
  });

  afterEach(() => {
    vi.runOnlyPendingTimers();
    vi.useRealTimers();
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('loads sale and active shop details before printing', () => {
    const fixture = createComponent();

    expect(saleService.getSaleById).toHaveBeenCalledWith('sale-1');
    expect(shopService.getShopDetails).toHaveBeenCalledWith('shop-1');
    expect(window.print).not.toHaveBeenCalled();

    fixture.detectChanges();
    vi.runOnlyPendingTimers();

    expect(fixture.componentInstance.sale()).toEqual(sale);
    expect(fixture.componentInstance.shop()).toEqual(shop);
    expect(window.print).toHaveBeenCalledTimes(1);
  });

  it('defaults missing or invalid template values to A4', () => {
    const missingFixture = createComponent();

    expect(missingFixture.componentInstance.template()).toBe('a4');

    TestBed.resetTestingModule();
    const invalidFixture = createComponent('unknown');

    expect(invalidFixture.componentInstance.template()).toBe('a4');
  });

  it('selects explicit A4 template', () => {
    const fixture = createComponent('a4');

    expect(fixture.componentInstance.template()).toBe('a4');
  });

  it('selects thermal template', () => {
    const fixture = createComponent('thermal');

    expect(fixture.componentInstance.template()).toBe('thermal');
  });

  it('renders thermal invoice component for thermal template', () => {
    const fixture = createComponent('thermal');

    fixture.detectChanges();
    vi.runOnlyPendingTimers();

    const thermalInvoice = fixture.nativeElement.querySelector('app-sale-invoice-thermal');
    const placeholder = fixture.nativeElement.textContent ?? '';

    expect(thermalInvoice).not.toBeNull();
    expect(placeholder).not.toContain('Thermal receipt preview');
  });

  it('sets an error and does not print when data loading fails', () => {
    saleService.getSaleById.mockReturnValue(throwError(() => ({ error: { detail: 'Sale missing' } })));

    const fixture = createComponent();
    fixture.detectChanges();
    vi.runOnlyPendingTimers();

    expect(fixture.componentInstance.errorMessage()).toBe('Sale missing');
    expect(fixture.componentInstance.isLoading()).toBe(false);
    expect(window.print).not.toHaveBeenCalled();
  });

  it('prints again when requested', () => {
    const fixture = createComponent();
    fixture.detectChanges();
    vi.runOnlyPendingTimers();
    vi.mocked(window.print).mockClear();

    fixture.componentInstance.onPrintAgain();

    expect(window.print).toHaveBeenCalledTimes(1);
  });
});
