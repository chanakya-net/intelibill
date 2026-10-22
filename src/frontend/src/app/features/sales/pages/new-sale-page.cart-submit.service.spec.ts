import '@angular/compiler';
import { Injectable, signal } from '@angular/core';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { EMPTY } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { NetworkStatusService } from '../../../core/services/network-status.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { ShopUpdatesSignalRService } from '../../../core/services/shop-updates-signalr.service';
import { CustomersFacade } from '../../customers/state/customers.facade';
import { InventoryService } from '../../inventory/services/inventory.service';
import { OfflineSaleStateService } from '../services/offline-sale-state.service';
import { SaleCartStateService } from '../services/sale-cart-state.service';
import { SalePreviewService } from '../services/sale-preview.service';
import { SaleService } from '../services/sale.service';
import { SalesFacade } from '../state/sales.facade';
import { NewSalePageCartSubmitService } from './new-sale-page.cart-submit.service';

@Injectable()
class TestCartSubmitService extends NewSalePageCartSubmitService {
  override onAddToCart(): void {}
  override onIncreaseCartItem(): void {}
  override onDecreaseCartItem(): void {}
  override onRemoveCartItem(): void {}
  override onRemoveServiceItem(): void {}
  override onServiceItemPriceChange(): void {}
  override onServiceItemQuantityChange(): void {}
  override onQuickProductTileSelected(): void {}
  override canIncreaseCartItem(): boolean { return false; }
  override hasTax(): boolean { return false; }
  override getLineSubtotal(): number { return 0; }
  override getLineTaxAmount(): number { return 0; }
  override getLineTotal(): number { return 0; }
  override getUnitSubtotal(): number { return 0; }
  override getUnitTaxAmount(): number { return 0; }
  override getUnitFinalPrice(): number { return 0; }
  override async onSubmit(): Promise<void> { return super.onSubmit(); }
  override createSaleIdempotencyKey(): string { return 'test-key'; }
  override onCancel(): void {}
  override toggleSaleDiscountEditor(): void {}
  override onSaleDiscountTypeChange(): void {}
  override onSaleDiscountValueChange(): void {}
  override isSaleDiscountEligible(): boolean { return false; }
  override getPreviewLine(): null { return null; }
  override toggleLineDiscountEditor(): void {}
  override isLineDiscountEditorOpen(): boolean { return false; }
  override getCartItemDiscountError(): string { return ''; }
  override getCartItemHsnError(): string { return ''; }
  override getCartItemTaxError(): string { return ''; }
  override getPaymentMethodLabel(): any { return 'Cash'; }
  override onCartItemDiscountTypeChange(): void {}
  override onCartItemDiscountValueChange(): void {}
  override onCartItemHsnCodeChange(): void {}
  override onCartItemTaxRateChange(): void {}
  override getLineDiscountLimits(): { maxFlat: number; maxPercent: number } { return { maxFlat: 0, maxPercent: 0 }; }
  override getSaleDiscountLimits(): { isEligible: boolean; maxFlat: number; maxPercent: number } {
    return { isEligible: false, maxFlat: 0, maxPercent: 0 };
  }
  override revalidateDiscountsAgainstPreview(): void {}
  override revalidateLineOverrides(): boolean { return false; }
  override getBlockingCartValidationErrorKey(): string | null { return null; }
  override normalizeHsnCode(): string | null { return null; }
  override normalizeAmount(): number { return 0; }
  override toFiniteAmount(value: number | null | undefined): number { return Number(value ?? 0); }
  override roundAmount(value: number): number { return value; }
  override areAmountsEqual(left: number, right: number): boolean { return left === right; }
  override syncPaymentSplitFromPaid(): void {}
  override syncPaymentSplitFromDue(): void {}
  override enforceNoCustomerCreditRestrictions(): void {}
  override resetSearchAndPickerState(): void {}
  override applyProductDefaultsForLine(): void {}
  override getEventNotificationKey(): string { return 'test'; }
  override detectAndHighlightChangedRows(): void {}
  override resetTransientState(): void {}
  override async searchOfflineCatalog(): Promise<void> {}
  override async refreshOfflinePreview(): Promise<void> {}
  override async onOfflineSubmit(): Promise<void> {}
  override printA4(): void {}
  override printThermal(): void {}
  override printOfflineA4(): void {}
  override printOfflineThermal(): void {}
}

function buildService() {
  TestBed.resetTestingModule();

  const route = { snapshot: { queryParamMap: convertToParamMap({}) } };
  const router = { navigate: vi.fn().mockResolvedValue(true) };
  const cart = signal([
    {
      barcode: 'SKU-1',
      batchNumber: 'B1',
      itemName: 'Test Item',
      quantity: 1,
      costPrice: 10,
      salesPrice: 100,
      mrp: 100,
      taxRatePercent: 5,
      taxIncluded: false,
      expiryDate: null,
      inventoryBatchId: 'batch-1',
      clientLineKey: 'line-1',
      itemDiscountType: 0,
      itemDiscountValue: 0,
      itemId: 'item-1',
      hsnCode: null,
    },
  ]);
  const recordSale = vi.fn();

  TestBed.configureTestingModule({
    imports: [ReactiveFormsModule],
    providers: [
      TestCartSubmitService,
      { provide: ActivatedRoute, useValue: route },
      { provide: Router, useValue: router },
      { provide: AuthService, useValue: { session: signal({ activeShopId: 'shop-1' }) } },
      { provide: NetworkStatusService, useValue: { canReachApi: vi.fn(() => true) } },
      { provide: ProductCatalogSyncService, useValue: { filterByName: vi.fn(() => []), filterByBarcode: vi.fn(() => []) } },
      { provide: ShopUpdatesSignalRService, useValue: { updates$: EMPTY, startConnection: vi.fn(), stopConnection: vi.fn() } },
      { provide: CustomersFacade, useValue: { allCustomers: signal([]), loadingCustomers: signal(false), loadCustomers: vi.fn() } },
      { provide: InventoryService, useValue: {} },
      {
        provide: SalesFacade,
        useValue: {
          submitting: signal(false),
          errorMessage: signal(''),
          lastMutationSucceeded: signal(false),
          lastMutationType: signal(null),
          lastRecordedSale: signal(null),
          recordSale,
        },
      },
      {
        provide: SaleCartStateService,
        useValue: {
          cart,
          serviceCart: signal([]),
          cartBootstrapped: signal(false),
          onClearCart: vi.fn(),
          getServiceLineSubtotal: vi.fn(() => 0),
          getServiceLineTaxAmount: vi.fn(() => 0),
          getServiceLineTotal: vi.fn(() => 0),
        },
      },
      {
        provide: SalePreviewService,
        useValue: {
          checkoutPreview: signal({
            totalAmount: 300,
            totalTaxableAmount: 300,
            totalTaxAmount: 0,
            totalDiscountAmount: 0,
            saleLevelEligibleSubtotal: 300,
            configuredSaleRule: null,
            lines: [],
            infos: [],
            warnings: [],
          }),
          isPreviewLoading: signal(false),
          previewError: signal(''),
          beginPreviewRequest: vi.fn(() => 1),
          finishPreviewRequest: vi.fn(),
          refreshOnlinePreview: vi.fn(),
          refreshOnServerUpdate: vi.fn(),
          clearPreviewState: vi.fn(),
          triggerServerUpdateRefresh: vi.fn(),
          clearPreviewError: vi.fn(),
          markPreviewInvalid: vi.fn(),
        },
      },
      {
        provide: OfflineSaleStateService,
        useValue: {
          offlineDeviceSettings: signal(null),
          snapshotCompletedAt: signal(null),
          offlinePendingCount: signal(0),
          offlineNeedsReviewCount: signal(0),
          offlineInvoiceRemaining: signal(0),
          offlineCatalog: signal([]),
          offlineCustomers: signal([]),
          searchOfflineCatalog: vi.fn(),
          buildOfflinePreview: vi.fn(),
          submitOfflineSale: vi.fn(),
          refreshSnapshot: vi.fn(),
        },
      },
      {
        provide: SaleService,
        useValue: {},
      },
    ],
  });

  const service = TestBed.inject(TestCartSubmitService);
  return { service, recordSale };
}

describe('NewSalePageCartSubmitService', () => {
  it('blocks submission when customer details are invalid', async () => {
    const { service, recordSale } = buildService();
    service.customerForm.controls.customerPhone.setValue('invalid-phone');

    await service.onSubmit();

    expect(service.customerForm.controls.customerPhone.touched).toBe(true);
    expect(recordSale).not.toHaveBeenCalled();
  });

  it('submits sale when payment split matches payable amount after credit notes', async () => {
    const { service, recordSale } = buildService();

    service.appliedCreditNotes.set([
      {
        creditNoteId: 'cn-1',
        code: 'CN-001',
        availableBalance: 100,
        expiresAt: null,
        status: 'Active',
        amount: 50,
      },
    ]);

    service.paymentForm.controls.paidAmount.setValue(250);
    service.paymentForm.controls.dueAmount.setValue(0);

    await service.onSubmit();

    expect(service.paymentSplitError()).toBe('');
    expect(recordSale).toHaveBeenCalledTimes(1);
    const payload = recordSale.mock.calls[0]?.[0];
    expect(payload?.paidAmount).toBe(250);
    expect(payload?.dueAmount).toBe(0);
    expect(payload?.creditNoteAppliedAmount).toBe(50);
    expect(payload?.creditNoteRedemptions).toHaveLength(1);
    expect(service.paymentSplitError()).toBe('');
  });

  it('blocks submit when payment split ignores payable credit-note amount', async () => {
    const { service, recordSale } = buildService();

    service.appliedCreditNotes.set([
      {
        creditNoteId: 'cn-1',
        code: 'CN-001',
        availableBalance: 100,
        expiresAt: null,
        status: 'Active',
        amount: 50,
      },
    ]);

    service.paymentForm.controls.paidAmount.setValue(150);
    service.paymentForm.controls.dueAmount.setValue(150);

    await service.onSubmit();

    expect(service.paymentSplitError()).toBe('sales.newSale.invalidPaymentSplit');
    expect(recordSale).not.toHaveBeenCalled();
  });
});
