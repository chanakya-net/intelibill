import '@angular/compiler';
import { Injectable, signal } from '@angular/core';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { EMPTY, of } from 'rxjs';
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
import { CheckoutIntentService } from '../services/checkout-intent.service';
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
  override async onOfflineSubmit(): Promise<void> { this.offlineSubmitCalls++; }
  offlineSubmitCalls = 0;
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
  const checkoutRecovered = vi.fn();
  const storedIntents = new Map<string, any>();
  const checkoutIntents = {
    prepare: vi.fn(async (scope: { userId: string; shopId: string }, request: any) => {
      const intent = {
        intentId: `intent-${storedIntents.size + 1}`,
        userId: scope.userId,
        shopId: scope.shopId,
        idempotencyKey: request.idempotencyKey,
        request,
        status: 'prepared',
        createdAt: new Date(storedIntents.size).toISOString(),
      };
      storedIntents.set(intent.intentId, intent);
      return intent;
    }),
    loadUnresolved: vi.fn(async (scope: { userId: string; shopId: string }) =>
      [...storedIntents.values()].filter(
        (intent) =>
          intent.userId === scope.userId &&
          intent.shopId === scope.shopId &&
          ['prepared', 'sending', 'uncertain'].includes(intent.status),
      ),
    ),
    get: vi.fn(async (intentId: string) => storedIntents.get(intentId) ?? null),
    recover: vi.fn(),
    changes$: EMPTY,
  };
  const session = signal<any>({ activeShopId: 'shop-1', user: { id: 'user-1' } });

  TestBed.configureTestingModule({
    imports: [ReactiveFormsModule],
    providers: [
      TestCartSubmitService,
      { provide: ActivatedRoute, useValue: route },
      { provide: Router, useValue: router },
      { provide: AuthService, useValue: { session } },
      { provide: CheckoutIntentService, useValue: checkoutIntents },
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
          lastRecordedIntentId: signal(null),
          recordSale,
          checkoutRecovered,
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
  return { service, recordSale, checkoutIntents, storedIntents, session, cart, checkoutRecovered };
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

  describe('durable checkout intent', () => {
    function readyToSubmit(service: TestCartSubmitService): void {
      service.paymentForm.controls.paidAmount.setValue(300);
      service.paymentForm.controls.dueAmount.setValue(0);
    }

    it('persists the intent before sending and sends the stored request', async () => {
      const { service, recordSale, checkoutIntents } = buildService();
      readyToSubmit(service);
      recordSale.mockImplementation(() => {
        expect(checkoutIntents.prepare).toHaveBeenCalledTimes(1);
      });

      await service.onSubmit();

      expect(checkoutIntents.prepare).toHaveBeenCalledWith(
        { userId: 'user-1', shopId: 'shop-1' },
        expect.objectContaining({ idempotencyKey: 'test-key' }),
      );
      const intent = await checkoutIntents.prepare.mock.results[0].value;
      expect(recordSale).toHaveBeenCalledWith(intent.request, intent.intentId);
      expect(service.activeCheckoutIntentId()).toBe(intent.intentId);
    });

    it('sends nothing and keeps the cart editable when the intent cannot be stored', async () => {
      const { service, recordSale, checkoutIntents } = buildService();
      readyToSubmit(service);
      checkoutIntents.prepare.mockRejectedValueOnce(new Error('QuotaExceededError'));

      await service.onSubmit();

      expect(recordSale).not.toHaveBeenCalled();
      expect(service.activeCheckoutIntentId()).toBeNull();
      expect(service.paymentSplitError()).toBe('sales.newSale.pendingCheckout.persistFailed');
    });

    it('blocks a replacement checkout while the cart checkout is unresolved', async () => {
      const { service, recordSale, checkoutIntents } = buildService();
      readyToSubmit(service);
      await service.onSubmit();
      service.paymentForm.controls.paidAmount.setValue(200);

      await service.onSubmit();

      expect(checkoutIntents.prepare).toHaveBeenCalledTimes(1);
      expect(recordSale).toHaveBeenCalledTimes(1);
      expect(service.paymentSplitError()).toBe('sales.newSale.pendingCheckout.blocksSubmit');
    });

    it('does not turn an unresolved online checkout into an offline invoice', async () => {
      const { service, recordSale } = buildService();
      readyToSubmit(service);
      await service.onSubmit();
      vi.spyOn(service, 'isOfflineMode').mockReturnValue(true);
      vi.spyOn(service, 'isOfflineEligible').mockReturnValue(true);

      await service.onSubmit();

      expect(service.offlineSubmitCalls).toBe(0);
      expect(recordSale).toHaveBeenCalledTimes(1);
      expect(service.paymentSplitError()).toBe('sales.newSale.pendingCheckout.blocksSubmit');
    });

    it('retries with the original stored request, not the edited cart', async () => {
      const { service, recordSale } = buildService();
      readyToSubmit(service);
      await service.onSubmit();
      await service.refreshPendingCheckouts();
      const [originalRequest, intentId] = recordSale.mock.calls[0];
      service.paymentForm.controls.paidAmount.setValue(10);

      service.onRetryCheckout(intentId);

      expect(recordSale).toHaveBeenCalledTimes(2);
      expect(recordSale.mock.calls[1]).toEqual([originalRequest, intentId]);
    });

    it('rebinds a restored cart to its unresolved checkout after a reload', async () => {
      const { service, storedIntents } = buildService();
      storedIntents.set('intent-from-before-reload', {
        intentId: 'intent-from-before-reload',
        userId: 'user-1',
        shopId: 'shop-1',
        status: 'uncertain',
        createdAt: '2026-11-01T00:00:00.000Z',
        request: { items: [{ clientLineKey: 'line-1' }], paidAmount: 300, dueAmount: 0 },
      });

      await service.refreshPendingCheckouts();

      expect(service.activeCheckoutIntentId()).toBe('intent-from-before-reload');
      expect(service.pendingCheckouts()).toHaveLength(1);
    });

    it('does not bind or show checkouts from another shop', async () => {
      const { service, storedIntents } = buildService();
      storedIntents.set('other-shop', {
        intentId: 'other-shop',
        userId: 'user-1',
        shopId: 'shop-2',
        status: 'uncertain',
        createdAt: '2026-11-01T00:00:00.000Z',
        request: { items: [{ clientLineKey: 'line-1' }], paidAmount: 300, dueAmount: 0 },
      });

      await service.refreshPendingCheckouts();

      expect(service.activeCheckoutIntentId()).toBeNull();
      expect(service.pendingCheckouts()).toHaveLength(0);
    });

    it('frees the cart for editing once its checkout is definitively rejected', async () => {
      const { service, storedIntents } = buildService();
      readyToSubmit(service);
      await service.onSubmit();
      const intentId = service.activeCheckoutIntentId()!;
      storedIntents.get(intentId).status = 'rejected';

      await service.refreshPendingCheckouts();

      expect(service.activeCheckoutIntentId()).toBeNull();
    });

    it('reconciles a checkout confirmed elsewhere instead of letting the cart submit again', async () => {
      const { service, storedIntents, checkoutIntents, checkoutRecovered } = buildService();
      readyToSubmit(service);
      await service.onSubmit();
      const intentId = service.activeCheckoutIntentId()!;
      storedIntents.get(intentId).status = 'confirmed';
      const sale = { saleId: 'sale-1', invoiceNumber: 'INV-1' };
      checkoutIntents.recover.mockReturnValue(of({ kind: 'replayed', intent: storedIntents.get(intentId), sale }));

      await service.refreshPendingCheckouts();

      expect(checkoutRecovered).toHaveBeenCalledWith(sale, intentId);
      expect(service.activeCheckoutIntentId()).toBe(intentId);
    });

    it('starting a new draft detaches the cart but keeps the checkout recoverable', async () => {
      const { service, checkoutIntents, cart } = buildService();
      readyToSubmit(service);
      await service.onSubmit();
      await service.refreshPendingCheckouts();
      vi.spyOn(service, 'resetTransientState').mockImplementation(() => cart.set([]));

      service.onStartNewDraft();
      await service.refreshPendingCheckouts();

      expect(service.activeCheckoutIntentId()).toBeNull();
      expect(service.pendingCheckouts()).toHaveLength(1);
      expect(checkoutIntents.prepare).toHaveBeenCalledTimes(1);
    });
  });
});
