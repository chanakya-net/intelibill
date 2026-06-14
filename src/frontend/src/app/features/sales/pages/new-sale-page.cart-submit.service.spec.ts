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
import { CreditNoteVerifyResponseDto } from '../services/sale.models';

@Injectable()
class TestCartSubmitService extends NewSalePageCartSubmitService {
  private creditNoteAppliedAmount = 0;
  private verifiedCreditNoteForTest: CreditNoteVerifyResponseDto | null = null;

  override onAddToCart(): void {}
  override onIncreaseCartItem(): void {}
  override onDecreaseCartItem(): void {}
  override onRemoveCartItem(): void {}
  override onRemoveServiceItem(): void {}
  override onServiceItemPriceChange(): void {}
  override onServiceItemQuantityChange(): void {}
  override onQuickProductTileSelected(): void {}
  override canIncreaseCartItem(): boolean {
    return false;
  }
  override hasTax(): boolean {
    return false;
  }
  override getLineSubtotal(): number {
    return 0;
  }
  override getLineTaxAmount(): number {
    return 0;
  }
  override getLineTotal(): number {
    return 0;
  }
  override getUnitSubtotal(): number {
    return 0;
  }
  override getUnitTaxAmount(): number {
    return 0;
  }
  override getUnitFinalPrice(): number {
    return 0;
  }
  override async onSubmit(): Promise<void> {
    await super.onSubmit();
  }
  override createSaleIdempotencyKey(): string {
    return 'test-sale-key';
  }
  override onCancel(): void {}
  override toggleSaleDiscountEditor(): void {}
  override onSaleDiscountTypeChange(): void {}
  override onSaleDiscountValueChange(): void {}
  override isSaleDiscountEligible(): boolean {
    return false;
  }
  override getPreviewLine(): null {
    return null;
  }
  override toggleLineDiscountEditor(): void {}
  override isLineDiscountEditorOpen(): boolean {
    return false;
  }
  override getCartItemDiscountError(): string {
    return '';
  }
  override getCartItemHsnError(): string {
    return '';
  }
  override getCartItemTaxError(): string {
    return '';
  }
  override getPaymentMethodLabel(): any {
    return 'Cash';
  }
  override onCartItemDiscountTypeChange(): void {}
  override onCartItemDiscountValueChange(): void {}
  override onCartItemHsnCodeChange(): void {}
  override onCartItemTaxRateChange(): void {}
  override getLineDiscountLimits(): { maxFlat: number; maxPercent: number } {
    return { maxFlat: 0, maxPercent: 0 };
  }
  override getSaleDiscountLimits(): { isEligible: boolean; maxFlat: number; maxPercent: number } {
    return { isEligible: false, maxFlat: 0, maxPercent: 0 };
  }
  override revalidateDiscountsAgainstPreview(): void {}
  override revalidateLineOverrides(): boolean {
    return false;
  }
  override getBlockingCartValidationErrorKey(): string | null {
    return null;
  }
  override normalizeHsnCode(): string | null {
    return null;
  }
  override normalizeAmount(value: number | null | undefined, _total: number): number {
    return Number.isFinite(Number(value ?? 0)) ? this.roundAmount(Math.max(0, Number(value))) : 0;
  }
  override toFiniteAmount(value: number | null | undefined): number {
    const amount = Number(value ?? 0);
    return Number.isFinite(amount) ? this.roundAmount(amount) : Number.NaN;
  }
  override roundAmount(value: number): number {
    return Number(value.toFixed(2));
  }
  override areAmountsEqual(left: number, right: number): boolean {
    return this.roundAmount(left) === this.roundAmount(right);
  }
  override syncPaymentSplitFromPaid(): void {}
  override syncPaymentSplitFromDue(): void {}
  override enforceNoCustomerCreditRestrictions(): void {}
  override resetSearchAndPickerState(): void {}
  override applyProductDefaultsForLine(): void {}
  override getEventNotificationKey(): string {
    return 'test';
  }
  override detectAndHighlightChangedRows(): void {}
  override async searchOfflineCatalog(): Promise<void> {}
  override async refreshOfflinePreview(): Promise<void> {}
  override async onOfflineSubmit(): Promise<void> {}
  override printA4(): void {}
  override printThermal(): void {}
  override printOfflineA4(): void {}
  override printOfflineThermal(): void {}

  override resetTransientState(): void {}

  setCreditNoteState(note: CreditNoteVerifyResponseDto | null, amount: number): void {
    this.verifiedCreditNoteForTest = note;
    this.creditNoteAppliedAmount = amount;
  }

  override getCreditNoteAppliedAmount(): number {
    return this.creditNoteAppliedAmount;
  }

  protected override getCreditNoteRedemptions() {
    if (!this.verifiedCreditNoteForTest || this.creditNoteAppliedAmount <= 0) {
      return [];
    }
    return [{ code: this.verifiedCreditNoteForTest.code, amount: this.creditNoteAppliedAmount }];
  }
}

const verifiedNote: CreditNoteVerifyResponseDto = {
  creditNoteId: 'cn-1',
  code: 'CN-001',
  availableBalance: 200,
  expiresAt: null,
  status: 'Active',
};

function buildService(): {
  service: TestCartSubmitService;
  salesFacade: { recordSale: ReturnType<typeof vi.fn> };
  salePreview: { checkoutPreview: ReturnType<typeof signal<any>> };
  cartState: {
    cart: ReturnType<typeof signal<any[]>>;
    serviceCart: ReturnType<typeof signal<any[]>>;
  };
} {
  TestBed.resetTestingModule();

  const route = {
    snapshot: { queryParamMap: convertToParamMap({}) },
  };

  const cartSignal = signal<any[]>([]);
  const serviceCartSignal = signal<any[]>([]);
  const checkoutPreview = signal<any>(null);
  const salePreview = {
    checkoutPreview,
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
  };

  const recordSale = vi.fn();

  TestBed.configureTestingModule({
    imports: [ReactiveFormsModule],
    providers: [
      TestCartSubmitService,
      { provide: ActivatedRoute, useValue: route },
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
          clearLastRecordedSale: vi.fn(),
          clearMutationStatus: vi.fn(),
        },
      },
      {
        provide: SaleCartStateService,
        useValue: {
          cart: cartSignal,
          serviceCart: serviceCartSignal,
          cartBootstrapped: signal(true),
          onClearCart: vi.fn(),
          getServiceLineSubtotal: vi.fn(() => 0),
          getServiceLineTaxAmount: vi.fn(() => 0),
          getServiceLineTotal: vi.fn(() => 0),
        },
      },
      { provide: SalePreviewService, useValue: salePreview },
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

  return {
    service: TestBed.inject(TestCartSubmitService),
    salesFacade: { recordSale },
    salePreview,
    cartState: {
      cart: cartSignal,
      serviceCart: serviceCartSignal,
    },
  };
}

describe('NewSalePageCartSubmitService', () => {
  it('includes credit note applied amount and redemption payload in recordSale request', async () => {
    const { service, salesFacade, cartState, salePreview } = buildService();
    const cartItem: any = {
      barcode: 'BC-1',
      batchNumber: 'B1',
      itemName: 'Item 1',
      quantity: 1,
      costPrice: 20,
      salesPrice: 100,
      mrp: 120,
      taxRatePercent: 18,
      taxIncluded: false,
      hsnCode: null,
      inventoryBatchId: 'inv-1',
      clientLineKey: 'line-1',
      itemDiscountType: 0,
      itemDiscountValue: 0,
    };
    cartState.cart.set([cartItem]);
    salePreview.checkoutPreview.set({
      totalAmount: 220,
      totalTaxableAmount: 200,
      totalTaxAmount: 20,
      totalDiscountAmount: 0,
      saleLevelEligibleSubtotal: 0,
      configuredSaleRule: null,
      lines: [],
      infos: [],
      warnings: [],
    });

    service.selectedCustomerId.set('cust-1');
    service.setCreditNoteState(verifiedNote, 70);
    service.paymentForm.controls.paidAmount.setValue(150);
    service.paymentForm.controls.dueAmount.setValue(0);
    service.saleDiscountType.set(0);
    service.saleDiscountValue.set(0);

    await service.onSubmit();

    expect(salesFacade.recordSale).toHaveBeenCalledWith(
      expect.objectContaining({
        paidAmount: 150,
        dueAmount: 0,
        creditNoteAppliedAmount: 70,
        creditNoteRedemptions: [{ code: 'CN-001', amount: 70 }],
      }),
    );
  });

  it('blocks submit when paid + due + note does not match total', async () => {
    const { service, cartState, salePreview } = buildService();
    const cartItem: any = {
      barcode: 'BC-1',
      batchNumber: 'B1',
      itemName: 'Item 1',
      quantity: 1,
      costPrice: 20,
      salesPrice: 100,
      mrp: 120,
      taxRatePercent: 18,
      taxIncluded: false,
      hsnCode: null,
      inventoryBatchId: 'inv-1',
      clientLineKey: 'line-1',
      itemDiscountType: 0,
      itemDiscountValue: 0,
    };
    cartState.cart.set([cartItem]);
    salePreview.checkoutPreview.set({
      totalAmount: 220,
      totalTaxableAmount: 200,
      totalTaxAmount: 20,
      totalDiscountAmount: 0,
      saleLevelEligibleSubtotal: 0,
      configuredSaleRule: null,
      lines: [],
      infos: [],
      warnings: [],
    });

    service.selectedCustomerId.set('cust-1');
    service.setCreditNoteState(verifiedNote, 70);
    service.paymentForm.controls.paidAmount.setValue(20);
    service.paymentForm.controls.dueAmount.setValue(40);

    await service.onSubmit();

    expect(service.paymentSplitError()).toBe('sales.newSale.invalidPaymentSplit');
  });
});
