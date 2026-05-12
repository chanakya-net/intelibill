import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { vi } from 'vitest';

const enIN = JSON.parse(readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8')) as Record<string, unknown>;

import { AuthService } from '../../../core/auth/auth.service';
import { SaleDto, SaleReturnDto, SaleReturnPreviewDto } from '../services/sale.service';
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
      itemId: 'item-1',
      itemName: 'Soap',
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
      returnedQuantity: 0,
      returnableQuantity: 2,
      returnStatus: 'Returnable',
    },
  ],
  returns: [],
  warnings: [],
  ...overrides,
});

const makePreview = (payoutAmount = 0): SaleReturnPreviewDto => ({
  saleId: 'sale-1',
  hasFinancialAccess: true,
  lines: [],
  financial: {
    totalRefundAmount: payoutAmount,
    dueReductionAmount: 0,
    payoutAmount,
    totalTaxableAmount: 0,
    totalTaxAmount: 0,
    customerBalanceBefore: null,
    customerBalanceAfter: null,
  },
  warnings: [],
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
    returnPreview.set(null);
    mutationType.set(null);
    mutationSucceeded.set(false);
    salesFacade.clearSaleReturnPreview.mockReset();
    salesFacade.clearMutationStatus.mockReset();
    salesFacade.previewSaleReturn.mockReset();
    salesFacade.recordSaleReturn.mockReset();
    salesFacade.voidSaleReturn.mockReset();

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

  it('shows before-discount and discount rows when totalDiscountAmount > 0', async () => {
    const { fixture, component } = await setup({
      totalBeforeDiscount: 240,
      totalDiscountAmount: 20,
      totalAmount: 220,
    });
    component.visible = true;
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Before Discount');
    expect(text).toContain('Discount');
    expect(text).toContain('Total Savings');
  });

  it('hides discount rows and shows price row when totalDiscountAmount is 0', async () => {
    const { fixture, component } = await setup({
      totalBeforeDiscount: 220,
      totalDiscountAmount: 0,
      totalAmount: 220,
    });
    component.visible = true;
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).not.toContain('Before Discount');
    expect(text).not.toContain('Total Savings');
  });

  it('shows line item savings when savingsAmount > 0', async () => {
    const { fixture, component } = await setup({
      totalBeforeDiscount: 240,
      totalDiscountAmount: 20,
      totalAmount: 220,
      items: [
        {
          saleItemId: 'line-1',
          itemId: 'item-1',
          itemName: 'Soap',
          inventoryBatchId: 'batch-1',
          quantity: 2,
          salesPrice: 100,
          originalSalesPrice: 110,
          finalSalesPrice: 100,
          preTaxAmountBeforeDiscount: 220,
          itemDiscountAmount: 20,
          saleDiscountAmount: 0,
          taxableAmount: 200,
          taxAmount: 20,
          totalAmount: 220,
          savingsAmount: 20,
          taxRatePercent: 10,
          isPriceIncludingTax: false,
          hasPriceMismatch: false,
          returnedQuantity: 0,
          returnableQuantity: 2,
          returnStatus: 'Returnable',
        },
      ],
    });
    component.visible = true;
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('20.00');
  });

  it('requires notes for wastage return lines before preview', async () => {
    const { component } = await setup();
    const item = selectedSale()!.items[0];

    component.openReturnPreview();
    component.toggleReturnLine(item, true);
    component.updateReturnCondition(item, 2);
    component.previewReturn();

    expect(component.validationMessages()).toContain('Soap notes are required for wastage, partial refund, or zero refund.');
    expect(salesFacade.previewSaleReturn).not.toHaveBeenCalled();
  });

  it('requires notes for partial and zero refunds before preview', async () => {
    const { component } = await setup();
    const item = selectedSale()!.items[0];

    component.openReturnPreview();
    component.toggleReturnLine(item, true);
    component.updateReturnCondition(item, 1);
    component.updateRefundAmount(item, 0);
    component.previewReturn();

    expect(component.validationMessages()).toContain('Soap notes are required for wastage, partial refund, or zero refund.');
    expect(salesFacade.previewSaleReturn).not.toHaveBeenCalled();
  });

  it('requires payout method when preview has payout amount', async () => {
    const { component } = await setup();
    const item = selectedSale()!.items[0];

    component.openReturnPreview();
    component.toggleReturnLine(item, true);
    component.updateReturnCondition(item, 1);
    component.updateNotes(item, 'Sealed');
    returnPreview.set(makePreview(110));
    component.submitReturn();

    expect(component.validationMessages()).toContain('Select a refund payout method.');
    expect(salesFacade.recordSaleReturn).not.toHaveBeenCalled();
  });

  it('requires due override confirmation and reason before submit', async () => {
    const { component } = await setup();
    const item = selectedSale()!.items[0];

    component.openReturnPreview();
    component.toggleReturnLine(item, true);
    component.updateReturnCondition(item, 1);
    component.updateNotes(item, 'Sealed');
    component.updateDueReductionOverride(10);
    returnPreview.set(makePreview(0));
    component.submitReturn();

    expect(component.validationMessages()).toContain('Confirm the due override before recording the return.');
    expect(component.validationMessages()).toContain('Enter a reason for the due override.');
    expect(salesFacade.recordSaleReturn).not.toHaveBeenCalled();
  });

  it('submits record return payload after successful preview', async () => {
    const { component } = await setup();
    const item = selectedSale()!.items[0];

    component.openReturnPreview();
    component.toggleReturnLine(item, true);
    component.updateReturnCondition(item, 1);
    component.updateNotes(item, 'Sealed');
    component.updatePayoutMethod(1);
    returnPreview.set(makePreview(110));
    component.submitReturn();

    expect(salesFacade.recordSaleReturn).toHaveBeenCalledWith('sale-1', {
      payoutMethod: 1,
      dueReductionOverrideAmount: null,
      dueOverrideReason: null,
      notes: null,
      items: [{ saleItemId: 'line-1', quantity: 2, condition: 1, approvedRefundAmount: 220, notes: 'Sealed' }],
    });
  });

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

  it('requires a reason before voiding a return', async () => {
    const saleReturn = makeReturn();
    const { component } = await setup({ returns: [saleReturn] });

    component.openVoidReturn(saleReturn);
    component.submitVoidReturn();

    expect(component.validationMessages()).toContain('Void reason is required.');
    expect(salesFacade.voidSaleReturn).not.toHaveBeenCalled();
  });

  it('submits void return payload with trimmed reason', async () => {
    const saleReturn = makeReturn();
    const { component } = await setup({ returns: [saleReturn] });

    component.openVoidReturn(saleReturn);
    component.updateVoidReason('  Duplicate return  ');
    component.submitVoidReturn();

    expect(salesFacade.voidSaleReturn).toHaveBeenCalledWith('sale-1', 'return-1', {
      reason: 'Duplicate return',
    });
  });

  it('closes void modal when void return succeeds', async () => {
    const saleReturn = makeReturn();
    const { component, fixture } = await setup({ returns: [saleReturn] });

    component.openVoidReturn(saleReturn);
    mutationType.set('void-return');
    mutationSucceeded.set(true);
    fixture.detectChanges();

    expect(component.showVoidReturn()).toBe(false);
    expect(salesFacade.clearMutationStatus).toHaveBeenCalled();
  });

  it('renders Print A4 button for selected sale', async () => {
    const { fixture, component } = await setup();
    component.visible = true;
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Print A4');
  });

  it('renders Print thermal button for selected sale', async () => {
    const { fixture, component } = await setup();
    component.visible = true;
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Print Thermal');
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
    selectedSale.set(null);
    const { component } = await setup({});
    const openSpy = vi.spyOn(window, 'open').mockImplementation(() => null);

    component.printA4();
    component.printThermal();

    expect(openSpy).not.toHaveBeenCalled();
    openSpy.mockRestore();
  });
});
