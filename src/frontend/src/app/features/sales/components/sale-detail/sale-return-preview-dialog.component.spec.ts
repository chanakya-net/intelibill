import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../../core/auth/auth.service';
import type { SaleDto } from '../../services/sale.models';
import { SalesFacade } from '../../state/sales.facade';
import { SaleReturnPreviewDialogComponent } from './sale-return-preview-dialog.component';

const enIN = JSON.parse(readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8')) as Record<string, unknown>;

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
  items: [
    {
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
    },
  ],
  returns: [],
  warnings: [],
});

describe('SaleReturnPreviewDialogComponent', () => {
  it('allows selecting service line without condition input and sends null condition payload', async () => {
    await TestBed.configureTestingModule({
      imports: [SaleReturnPreviewDialogComponent, TranslocoTestingModule.forRoot({ langs: { 'en-IN': enIN }, preloadLangs: true })],
      providers: [
        { provide: SalesFacade, useClass: SalesFacadeStub },
        { provide: AuthService, useClass: AuthServiceStub },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SaleReturnPreviewDialogComponent);
    const component = fixture.componentInstance;
    const facade = TestBed.inject(SalesFacade) as unknown as SalesFacadeStub;
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
});
