import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../../core/auth/auth.service';
import type { SaleDto, SaleItemDto } from '../../services/sale.models';
import { SalesFacade } from '../../state/sales.facade';
import { SaleReturnPreviewDialogComponent } from './sale-return-preview-dialog.component';

const enIN = JSON.parse(
  readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8'),
) as Record<string, unknown>;

class SalesFacadeStub {
  readonly returnPreview = signal(null);
  readonly loadingReturnPreview = signal(false);
  readonly submitting = signal(false);
  readonly returnPreviewErrorMessage = signal<string | null>(null);
  readonly lastMutationSucceeded = signal(false);
  readonly lastMutationType = signal<string | null>(null);
  readonly previewSaleReturn = vi.fn();
  readonly clearSaleReturnPreview = vi.fn();
  readonly recordSaleReturn = vi.fn();
  readonly clearMutationStatus = vi.fn();
}

class AuthServiceStub {
  readonly session = signal({
    activeShopId: 'shop-1',
    shops: [{ shopId: 'shop-1', role: 'Owner', isDefault: true }],
  });
}

const makeServiceItem = (overrides: Partial<SaleItemDto> = {}): SaleItemDto => ({
  saleItemId: 'service-line-1',
  lineType: 'Service',
  itemId: null,
  serviceId: 'svc-1',
  itemName: 'Consultation',
  lineCode: 'SRV-001',
  inventoryBatchId: null,
  quantity: 1,
  salesPrice: 200,
  originalSalesPrice: 200,
  finalSalesPrice: 200,
  preTaxAmountBeforeDiscount: 200,
  itemDiscountAmount: 0,
  saleDiscountAmount: 0,
  taxableAmount: 200,
  taxAmount: 0,
  totalAmount: 200,
  savingsAmount: 0,
  taxRatePercent: 0,
  isPriceIncludingTax: true,
  hasPriceMismatch: false,
  returnedQuantity: 0,
  returnableQuantity: 1,
  returnStatus: 'NotReturned',
  hsnCode: null,
  ...overrides,
});

const makeGoodsItem = (overrides: Partial<SaleItemDto> = {}): SaleItemDto => ({
  saleItemId: 'goods-line-1',
  lineType: 'Goods',
  itemId: 'item-1',
  serviceId: null,
  itemName: 'Soap',
  lineCode: 'ITEM-001',
  inventoryBatchId: 'batch-1',
  quantity: 2,
  salesPrice: 110,
  originalSalesPrice: 110,
  finalSalesPrice: 110,
  preTaxAmountBeforeDiscount: 220,
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
  returnStatus: 'NotReturned',
  hsnCode: null,
  ...overrides,
});

const makeSale = (): SaleDto => ({
  saleId: 'sale-1',
  invoiceNumber: 'INV-1',
  customerId: null,
  customerName: null,
  customerPhone: null,
  paymentMethod: 1,
  soldAt: '2026-05-05T10:00:00Z',
  paidAmount: 200,
  dueAmount: 0,
  totalBeforeDiscount: 200,
  totalDiscountAmount: 0,
  totalAmount: 200,
  totalTaxAmount: 0,
  items: [makeServiceItem()],
  returns: [],
  warnings: [],
});

async function createComponent() {
  await TestBed.configureTestingModule({
    imports: [
      SaleReturnPreviewDialogComponent,
      TranslocoTestingModule.forRoot({
        langs: { 'en-IN': enIN },
        translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
        preloadLangs: true,
      }),
    ],
    providers: [
      { provide: SalesFacade, useClass: SalesFacadeStub },
      { provide: AuthService, useClass: AuthServiceStub },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(SaleReturnPreviewDialogComponent);
  const component = fixture.componentInstance;
  const facade = TestBed.inject(SalesFacade) as unknown as SalesFacadeStub;
  return { fixture, component, facade };
}

describe('SaleReturnPreviewDialogComponent', () => {
  it('allows selecting service line without condition input and sends null condition payload', async () => {
    const { fixture, component, facade } = await createComponent();
    component.sale = makeSale();
    component.visible = true;
    fixture.detectChanges();

    const serviceItem = component.sale!.items[0];
    component.toggleReturnLine(serviceItem, true);
    component.previewReturn();

    expect(facade.previewSaleReturn).toHaveBeenCalledTimes(1);
    const payload = facade.previewSaleReturn.mock.calls[0][1];
    expect(payload.items[0].lineType).toBe('Service');
    expect(payload.items[0].condition).toBeNull();
  });

  it('for mixed sale: service line passes null condition, goods line sends selected condition', async () => {
    const { fixture, component, facade } = await createComponent();
    component.sale = {
      ...makeSale(),
      items: [makeServiceItem(), makeGoodsItem()],
    };
    component.visible = true;
    fixture.detectChanges();

    const [serviceItem, goodsItem] = component.sale!.items;
    component.toggleReturnLine(serviceItem, true);
    component.toggleReturnLine(goodsItem, true);
    component.updateReturnCondition(
      goodsItem,
      1 as unknown as Parameters<typeof component.updateReturnCondition>[1],
    );
    component.previewReturn();

    expect(facade.previewSaleReturn).toHaveBeenCalledTimes(1);
    const payload = facade.previewSaleReturn.mock.calls[0][1];
    const serviceLine = payload.items.find((i: { lineType: string }) => i.lineType === 'Service');
    const goodsLine = payload.items.find((i: { lineType: string }) => i.lineType === 'Goods');
    expect(serviceLine.condition).toBeNull();
    expect(goodsLine.condition).not.toBeNull();
  });

  it('service line validation does not require condition when previewing', async () => {
    const { fixture, component } = await createComponent();
    component.sale = makeSale();
    component.visible = true;
    fixture.detectChanges();

    const serviceItem = component.sale!.items[0];
    component.toggleReturnLine(serviceItem, true);

    const errors = (
      component as unknown as { validateReturnDrafts(): string[] }
    ).validateReturnDrafts();

    expect(
      errors.find((e) => e.includes('condition') || e.includes('Consultation')),
    ).toBeUndefined();
  });

  it('returns validation error for goods line with no condition selected', async () => {
    const { fixture, component } = await createComponent();
    component.sale = { ...makeSale(), items: [makeGoodsItem()] };
    component.visible = true;
    fixture.detectChanges();

    const goodsItem = component.sale!.items[0];
    component.toggleReturnLine(goodsItem, true);

    const errors = (
      component as unknown as { validateReturnDrafts(): string[] }
    ).validateReturnDrafts();

    expect(errors.some((e) => e.toLowerCase().includes('condition') || e.includes('Soap'))).toBe(
      true,
    );
  });

  it('service line shows correct returned and returnable quantities', async () => {
    const { fixture, component } = await createComponent();
    component.sale = {
      ...makeSale(),
      items: [makeServiceItem({ returnedQuantity: 1, returnableQuantity: 1, quantity: 2 })],
    };
    component.visible = true;
    fixture.detectChanges();

    const item = component.sale!.items[0];
    expect(component.isFullyReturned(item)).toBe(false);
    expect(item.returnedQuantity).toBe(1);
    expect(item.returnableQuantity).toBe(1);
  });

  it('renders return quantity control without raw translation keys', async () => {
    const { fixture, component } = await createComponent();
    component.sale = makeSale();
    component.visible = true;
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Qty to return');
    expect(text).not.toContain('sales.returns.preview.col.quantity');
    expect(text).not.toContain('sales.returns.preview.col.returnQty');
  });

  it('shows the selected quantity inside the return stepper', async () => {
    const { fixture, component } = await createComponent();
    component.sale = {
      ...makeSale(),
      items: [makeGoodsItem({ quantity: 10, returnableQuantity: 10 })],
    };
    component.visible = true;
    fixture.detectChanges();

    const item = component.sale.items[0];
    component.toggleReturnLine(item, true);
    component.updateReturnQuantity(item, 2);
    fixture.detectChanges();

    const quantityInput = fixture.nativeElement.querySelector(
      '.return-preview-stepper input',
    ) as HTMLInputElement | null;
    expect(quantityInput?.value).toBe('2');
  });

  it('service line with zero returnable quantity is fully returned', async () => {
    const { fixture, component } = await createComponent();
    component.sale = {
      ...makeSale(),
      items: [makeServiceItem({ returnedQuantity: 1, returnableQuantity: 0 })],
    };
    component.visible = true;
    fixture.detectChanges();

    const item = component.sale!.items[0];
    expect(component.isFullyReturned(item)).toBe(true);
  });
});
