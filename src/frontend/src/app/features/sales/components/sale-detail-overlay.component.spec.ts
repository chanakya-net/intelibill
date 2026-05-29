import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { vi } from 'vitest';

const enIN = JSON.parse(readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8')) as Record<string, unknown>;

import { AuthService } from '../../../core/auth/auth.service';
import type { SaleDto, SaleReturnDto, SaleReturnPreviewDto } from '../services/sale.models';
import { SalesFacade } from '../state/sales.facade';
import { SaleDetailOverlayComponent } from './sale-detail-overlay.component';

const makeReturn = (overrides: Partial<SaleReturnDto> = {}): SaleReturnDto => ({
  saleReturnId: 'return-1',
  returnNumber: 'RET-1',
  returnedAt: '2026-05-05T11:00:00Z',
  totalRefundAmount: 110,
  dueReductionAmount: 0,
  payoutAmount: 110,
  isVoided: false,
  voidedAt: null,
  voidReason: null,
  items: [
    {
      saleReturnItemId: 'return-line-1',
      saleItemId: 'line-1',
      quantity: 1,
      condition: 1,
      approvedRefundAmount: 110,
      notes: 'Sealed',
    },
  ],
  ...overrides,
});

const makeSale = (overrides: Partial<SaleDto> = {}): SaleDto => ({
  saleId: 'sale-1',
  invoiceNumber: 'INV-1',
  customerId: 'customer-1',
  customerName: 'Alice',
  customerPhone: '+919999111222',
  paymentMethod: 1,
  soldAt: '2026-05-05T10:00:00Z',
  paidAmount: 220,
  dueAmount: 0,
  totalBeforeDiscount: 220,
  totalDiscountAmount: 0,
  totalAmount: 220,
  totalTaxAmount: 20,
  items: [
    {
      saleItemId: 'line-1',
      lineType: 1,
      itemId: 'item-1',
      serviceId: null,
      itemName: 'Soap',
      lineCode: 'ITEM-001',
      inventoryBatchId: 'batch-1',
      quantity: 2,
      salesPrice: 100,
      originalSalesPrice: 100,
      finalSalesPrice: 100,
      preTaxAmountBeforeDiscount: 200,
      itemDiscountAmount: 0,
      saleDiscountAmount: 0,
      taxableAmount: 200,
      taxAmount: 20,
      totalAmount: 220,
      savingsAmount: 0,
      taxRatePercent: 10,
      isPriceIncludingTax: false,
      hasPriceMismatch: false,
      hsnCode: null,
      returnedQuantity: 0,
      returnableQuantity: 2,
      returnStatus: 'Returnable',
    },
  ],
  returns: [],
  warnings: [],
  ...overrides,
});

describe('SaleDetailOverlayComponent', () => {
  const selectedSale = signal<SaleDto | null>(makeSale());
  const returnPreview = signal<SaleReturnPreviewDto | null>(null);
  const mutationType = signal<'record-sale' | 'record-return' | 'void-return' | null>(null);
  const mutationSucceeded = signal(false);

  const salesFacade = {
    selectedSale,
    loadingSaleDetail: signal(false),
    returnPreview,
    loadingReturnPreview: signal(false),
    submitting: signal(false),
    returnPreviewErrorMessage: signal(''),
    lastMutationType: mutationType,
    lastMutationSucceeded: mutationSucceeded,
    clearSaleReturnPreview: vi.fn(),
    clearMutationStatus: vi.fn(),
    previewSaleReturn: vi.fn(),
    recordSaleReturn: vi.fn(),
    voidSaleReturn: vi.fn(),
  };

  const authService = {
    session: signal({
      accessToken: 'token',
      refreshToken: 'refresh',
      accessTokenExpiresAt: '',
      refreshTokenExpiresAt: '',
      rememberMe: false,
      user: { id: 'user-1', email: 'owner@test.com', phoneNumber: null, firstName: 'Owner', lastName: 'User' },
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Shop', role: 'Owner', isDefault: true, lastUsedAt: null }],
    }),
  };

  async function setup(saleOverrides: Partial<SaleDto> | null = {}) {
    selectedSale.set(saleOverrides === null ? null : makeSale(saleOverrides));

    await TestBed.configureTestingModule({
      imports: [
        SaleDetailOverlayComponent,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
      ],
      providers: [
        { provide: SalesFacade, useValue: salesFacade },
        { provide: AuthService, useValue: authService },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SaleDetailOverlayComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    return { fixture, component };
  }

  it('shows return history with void action for active returns and marks voided returns', async () => {
    const activeReturn = makeReturn();
    const voidedReturn = makeReturn({
      saleReturnId: 'return-2',
      returnNumber: 'RET-2',
      isVoided: true,
      voidedAt: '2026-05-05T12:00:00Z',
      voidReason: 'Duplicate return',
    });
    const { component, fixture } = await setup({ returns: [activeReturn, voidedReturn] });

    component.visible = true;
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Return history');
    expect(text).toContain('RET-1');
    expect(text).toContain('RET-2');
    expect(text).toContain('Voided');
    expect(text).toContain('Reason: Duplicate return');
    expect(fixture.nativeElement.querySelector('[aria-label="Void RET-1"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[aria-label="Void RET-2"]')).toBeNull();
  });

  it('opens A4 print page when printA4 is called', async () => {
    const { component } = await setup();
    const openSpy = vi.spyOn(window, 'open').mockImplementation(() => null);

    component.printA4();

    expect(openSpy).toHaveBeenCalledWith('/sales/sale-1/print?template=a4', '_blank');
    openSpy.mockRestore();
  });

  it('opens thermal print page when printThermal is called', async () => {
    const { component } = await setup();
    const openSpy = vi.spyOn(window, 'open').mockImplementation(() => null);

    component.printThermal();

    expect(openSpy).toHaveBeenCalledWith('/sales/sale-1/print?template=thermal', '_blank');
    openSpy.mockRestore();
  });

  it('does not call window.open if sale is not available', async () => {
    const { component } = await setup(null);
    const openSpy = vi.spyOn(window, 'open').mockImplementation(() => null);

    component.printA4();
    component.printThermal();

    expect(openSpy).not.toHaveBeenCalled();
    openSpy.mockRestore();
  });
});
