import '@angular/compiler';
import { Injectable, signal } from '@angular/core';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { EMPTY, of, throwError } from 'rxjs';
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
import { NewSalePageCreditNoteService } from './new-sale-page.credit-note.service';
import { CreditNoteVerifyResponseDto } from '../services/sale.models';

@Injectable()
class TestCreditNoteService extends NewSalePageCreditNoteService {
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
  override async onSubmit(): Promise<void> {}
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
  override getSaleDiscountLimits(): { isEligible: boolean; maxFlat: number; maxPercent: number } { return { isEligible: false, maxFlat: 0, maxPercent: 0 }; }
  override revalidateDiscountsAgainstPreview(): void {}
  override revalidateLineOverrides(): boolean { return false; }
  override getBlockingCartValidationErrorKey(): string | null { return null; }
  override normalizeHsnCode(): string | null { return null; }
  override normalizeAmount(): number { return 0; }
  override toFiniteAmount(): number { return 0; }
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

const verifiedNote: CreditNoteVerifyResponseDto = {
  creditNoteId: 'cn-1',
  code: 'CN-ABC-123',
  balanceAmount: 250,
  expiresAt: '2027-01-01T00:00:00Z',
  status: 'Active',
};

function buildService(saleServiceOverrides: Partial<{ verifyCreditNote: ReturnType<typeof vi.fn> }> = {}) {
  TestBed.resetTestingModule();

  const saleService = {
    verifyCreditNote: vi.fn(() => of(verifiedNote)),
    ...saleServiceOverrides,
  };

  const route = {
    snapshot: { queryParamMap: convertToParamMap({}) },
  };

  TestBed.configureTestingModule({
    imports: [ReactiveFormsModule],
    providers: [
      TestCreditNoteService,
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
        },
      },
      {
        provide: SaleCartStateService,
        useValue: {
          cart: signal([]),
          serviceCart: signal([]),
          cartBootstrapped: signal(false),
          getServiceLineSubtotal: vi.fn(() => 0),
          getServiceLineTaxAmount: vi.fn(() => 0),
          getServiceLineTotal: vi.fn(() => 0),
        },
      },
      {
        provide: SalePreviewService,
        useValue: {
          checkoutPreview: signal(null),
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
      { provide: SaleService, useValue: saleService },
    ],
  });

  return { service: TestBed.inject(TestCreditNoteService), saleService };
}

describe('NewSalePageCreditNoteService', () => {
  it('updates creditNoteCode signal and clears prior result on code change', () => {
    const { service } = buildService();
    service.verifiedCreditNote.set(verifiedNote);
    service.creditNoteError.set('some error');

    service.onCreditNoteCodeChange('CN-NEW-456');

    expect(service.creditNoteCode()).toBe('CN-NEW-456');
    expect(service.verifiedCreditNote()).toBeNull();
    expect(service.creditNoteError()).toBe('');
  });

  it('calls verify API and populates verifiedCreditNote on success', async () => {
    const { service, saleService } = buildService();
    service.creditNoteCode.set('CN-ABC-123');

    await service.onVerifyCreditNote();

    expect(saleService.verifyCreditNote).toHaveBeenCalledWith({ code: 'CN-ABC-123' });
    expect(service.verifiedCreditNote()).toEqual(verifiedNote);
    expect(service.creditNoteError()).toBe('');
    expect(service.isCreditNoteVerifying()).toBe(false);
  });

  it('sets error signal and clears verifiedCreditNote on API failure', async () => {
    const { service } = buildService({
      verifyCreditNote: vi.fn(() => throwError(() => new Error('not found'))),
    });
    service.creditNoteCode.set('BAD-CODE');

    await service.onVerifyCreditNote();

    expect(service.verifiedCreditNote()).toBeNull();
    expect(service.creditNoteError()).toBe('sales.newSale.creditNote.verifyError');
    expect(service.isCreditNoteVerifying()).toBe(false);
  });

  it('skips API call when code is blank', async () => {
    const { service, saleService } = buildService();
    service.creditNoteCode.set('');

    await service.onVerifyCreditNote();

    expect(saleService.verifyCreditNote).not.toHaveBeenCalled();
  });

  it('resets all credit note state', () => {
    const { service } = buildService();
    service.creditNoteCode.set('CN-ABC');
    service.verifiedCreditNote.set(verifiedNote);
    service.creditNoteError.set('err');

    service.resetCreditNoteState();

    expect(service.creditNoteCode()).toBe('');
    expect(service.verifiedCreditNote()).toBeNull();
    expect(service.creditNoteError()).toBe('');
    expect(service.isCreditNoteVerifying()).toBe(false);
  });
});
