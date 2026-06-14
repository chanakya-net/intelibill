import '@angular/compiler';
import { Injectable, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { convertToParamMap, ActivatedRoute, Router } from '@angular/router';
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
  override createSaleIdempotencyKey(): string { return 'sale-key'; }
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
  override getSaleDiscountLimits(): { isEligible: boolean; maxFlat: number; maxPercent: number } { return { isEligible: false, maxFlat: 0, maxPercent: 0 }; }
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
  override getEventNotificationKey(): string { return 'updated'; }
  override detectAndHighlightChangedRows(): void {}
  override async searchOfflineCatalog(): Promise<void> {}
  override async refreshOfflinePreview(): Promise<void> {}
  override async onOfflineSubmit(): Promise<void> {}
  override printA4(): void {}
  override printThermal(): void {}
  override printOfflineA4(): void {}
  override printOfflineThermal(): void {}
  override resetTransientState(): void {}
}

function setup() {
  TestBed.resetTestingModule();
  const recordSale = vi.fn();
  TestBed.configureTestingModule({
    imports: [ReactiveFormsModule],
    providers: [
      TestCartSubmitService,
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({}) } } },
      { provide: Router, useValue: { navigate: vi.fn().mockResolvedValue(true) } },
      { provide: AuthService, useValue: { session: signal({ activeShopId: 'shop-1' }) } },
      { provide: NetworkStatusService, useValue: { canReachApi: vi.fn(() => true) } },
      { provide: ProductCatalogSyncService, useValue: { filterByName: vi.fn(() => []), filterByBarcode: vi.fn(() => []) } },
      { provide: ShopUpdatesSignalRService, useValue: { updates$: EMPTY, startConnection: vi.fn(), stopConnection: vi.fn() } },
      { provide: CustomersFacade, useValue: { allCustomers: signal([]), loadingCustomers: signal(false) } },
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
          cart: signal([{ clientLineKey: 'line-1', barcode: 'BC-1', itemName: 'Rice', batchNumber: 'B-1', inventoryBatchId: 'batch-1', quantity: 1, availableQuantity: 1, salesPrice: 100, mrp: 100, taxRatePercent: 5, taxIncluded: false, costPrice: 80, itemDiscountType: 0, itemDiscountValue: 0, hsnCode: null }]),
          serviceCart: signal([]),
          cartBootstrapped: signal(true),
          onClearCart: vi.fn(),
          getServiceLineSubtotal: vi.fn(() => 0),
          getServiceLineTaxAmount: vi.fn(() => 0),
          getServiceLineTotal: vi.fn(() => 0),
        },
      },
      {
        provide: SalePreviewService,
        useValue: {
          checkoutPreview: signal({ totalAmount: 100, totalTaxAmount: 0, totalDiscountAmount: 0, totalTaxableAmount: 100, saleLevelEligibleSubtotal: 100, configuredSaleRule: null, lines: [], infos: [], warnings: [] }),
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
      { provide: SaleService, useValue: {} },
      { provide: OfflineSaleStateService, useValue: { submitOfflineSale: vi.fn(), refreshSnapshot: vi.fn(), searchOfflineCatalog: vi.fn(), offlineDeviceSettings: signal(null), snapshotCompletedAt: signal(null), offlinePendingCount: signal(0), offlineNeedsReviewCount: signal(0), offlineInvoiceRemaining: signal(0), offlineCatalog: signal([]), offlineCustomers: signal([]) } },
    ],
  });

  return { service: TestBed.inject(TestCartSubmitService), recordSale };
}

describe('NewSalePageCartSubmitService', () => {
  it('includes the mismatch confirmation flag when submitting a confirmed credit-note sale', async () => {
    const { service, recordSale } = setup();
    service.paymentForm.controls.paymentMethod.setValue(4);
    service.paymentForm.controls.paidAmount.setValue(100);
    service.paymentForm.controls.dueAmount.setValue(0);
    service.creditNoteCustomerMismatchWarning.set(true);
    service.creditNoteCustomerMismatchConfirmed.set(true);
    service.selectedCustomerId.set('cust-1');
    service.customerForm.controls.customerName.setValue('Acme');

    await service.onSubmit();

    expect(recordSale).toHaveBeenCalledWith(expect.objectContaining({
      creditNoteCustomerMismatchConfirmed: true,
      customerName: 'Acme',
    }));
  });

  it('blocks submission when mismatch is not confirmed', async () => {
    const { service, recordSale } = setup();
    service.paymentForm.controls.paidAmount.setValue(100);
    service.paymentForm.controls.dueAmount.setValue(0);
    service.creditNoteCustomerMismatchWarning.set(true);
    service.creditNoteCustomerMismatchConfirmed.set(false);
    service.selectedCustomerId.set('cust-1');
    service.customerForm.controls.customerName.setValue('Acme');

    await service.onSubmit();

    expect(recordSale).not.toHaveBeenCalled();
    expect(service.paymentSplitError()).toBe('sales.newSale.creditNote.customerMismatchConfirmRequired');
  });
});
