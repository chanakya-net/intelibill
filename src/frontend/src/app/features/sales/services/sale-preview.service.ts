import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subject, catchError, debounceTime, map, of, switchMap } from 'rxjs';

import { NetworkStatusService } from '../../../core/services/network-status.service';
import { CartItem } from './sale-cart-state.service';
import { InstantDiscountRequest, PreviewSaleRequest, SalePreviewDto, SaleService } from './sale.service';

type PreviewRequestResult =
  | { readonly requestId: number; readonly preview: SalePreviewDto; readonly failed?: false }
  | { readonly requestId: number; readonly preview: null; readonly failed: true; readonly errorMessage: string };

interface PreviewSetupOptions {
  readonly destroyRef: DestroyRef;
  readonly getCart: () => readonly CartItem[];
  readonly getSaleDiscount: () => InstantDiscountRequest;
  readonly onPreviewApplied?: (preview: SalePreviewDto, oldPreview: SalePreviewDto | null) => void;
}

interface FinishPreviewOptions {
  readonly failed?: boolean;
  readonly errorMessage?: string;
  readonly onPreviewApplied?: (preview: SalePreviewDto, oldPreview: SalePreviewDto | null) => void;
}

@Injectable({ providedIn: 'root' })
export class SalePreviewService {
  private readonly saleService = inject(SaleService);
  private readonly networkStatus = inject(NetworkStatusService);
  private readonly previewTrigger$ = new Subject<void>();
  private readonly serverUpdateTrigger$ = new Subject<void>();
  private readonly previewRequestState = { latestRequestId: 0 };
  private readonly previewWireupRefs = new WeakSet<DestroyRef>();
  private readonly serverUpdateWireupRefs = new WeakSet<DestroyRef>();

  readonly checkoutPreview = signal<SalePreviewDto | null>(null);
  readonly isPreviewLoading = signal(false);
  readonly previewError = signal('');

  triggerPreview(): void {
    this.previewTrigger$.next();
  }

  triggerServerUpdateRefresh(): void {
    this.serverUpdateTrigger$.next();
  }

  refreshOnlinePreview(options: PreviewSetupOptions): void {
    if (this.previewWireupRefs.has(options.destroyRef)) {
      return;
    }
    this.previewWireupRefs.add(options.destroyRef);

    this.previewTrigger$
      .pipe(
        debounceTime(300),
        switchMap(() => this.buildPreviewRequestStream(options)),
        takeUntilDestroyed(options.destroyRef),
      )
      .subscribe((result: PreviewRequestResult | null) => {
        if (result === null) {
          return;
        }

        if (result.preview === null) {
          this.finishPreviewRequest(result.requestId, null, {
            failed: !!result.failed,
            errorMessage: result.errorMessage,
          });
          return;
        }

        this.finishPreviewRequest(result.requestId, result.preview, {
          onPreviewApplied: options.onPreviewApplied,
        });
      });
  }

  refreshOnServerUpdate(options: PreviewSetupOptions): void {
    if (this.serverUpdateWireupRefs.has(options.destroyRef)) {
      return;
    }
    this.serverUpdateWireupRefs.add(options.destroyRef);

    this.serverUpdateTrigger$
      .pipe(
        switchMap(() => this.buildPreviewRequestStream(options)),
        takeUntilDestroyed(options.destroyRef),
      )
      .subscribe((result: PreviewRequestResult | null) => {
        if (result === null) {
          return;
        }

        if (result.preview === null) {
          this.finishPreviewRequest(result.requestId, null, {
            failed: !!result.failed,
            errorMessage: result.errorMessage,
          });
          return;
        }

        this.finishPreviewRequest(result.requestId, result.preview, {
          onPreviewApplied: options.onPreviewApplied,
        });
      });
  }

  beginPreviewRequest(): number {
    const requestId = ++this.previewRequestState.latestRequestId;
    this.isPreviewLoading.set(true);
    this.previewError.set('');
    return requestId;
  }

  finishPreviewRequest(
    requestId: number,
    preview: SalePreviewDto | null,
    options: FinishPreviewOptions = {},
  ): void {
    if (requestId !== this.previewRequestState.latestRequestId) {
      return;
    }

    const oldPreview = this.checkoutPreview();

    if (preview === null) {
      this.isPreviewLoading.set(false);
      this.previewError.set(options.failed ? options.errorMessage ?? 'sales.newSale.previewError' : '');
      if (options.failed) {
        this.checkoutPreview.set(null);
      }
      return;
    }

    this.checkoutPreview.set(preview);
    options.onPreviewApplied?.(preview, oldPreview);
    this.isPreviewLoading.set(false);
  }

  extractPreviewErrorMessage(error: unknown): string {
    if (typeof error !== 'object' || error === null) {
      return 'sales.newSale.previewError';
    }

    const errorLike = error as { error?: { detail?: unknown; title?: unknown; errors?: unknown } };
    const detail = errorLike.error?.detail;
    if (typeof detail === 'string' && detail.trim()) {
      return detail;
    }

    const errors = errorLike.error?.errors;
    if (Array.isArray(errors)) {
      const firstDescription = errors
        .map((entry) => (typeof entry === 'object' && entry !== null
          ? (entry as { description?: unknown }).description
          : null))
        .find((value): value is string => typeof value === 'string' && value.trim().length > 0);

      if (firstDescription) {
        return firstDescription;
      }
    }

    const title = errorLike.error?.title;
    if (typeof title === 'string' && title.trim()) {
      return title;
    }

    return 'sales.newSale.previewError';
  }

  buildPreviewRequest(
    cart: readonly CartItem[],
    saleDiscountType: 0 | 1 | 2,
    saleDiscountValue: number,
  ): PreviewSaleRequest {
    return {
      saleDiscount: { type: saleDiscountType, value: saleDiscountValue },
      items: cart.map((item) => ({
        inventoryBatchId: item.inventoryBatchId,
        barcode: item.barcode,
        batchNumber: item.batchNumber,
        itemName: item.itemName,
        quantity: item.quantity,
        costPrice: item.costPrice,
        salesPrice: item.salesPrice,
        mrp: item.mrp,
        taxRatePercent: item.taxRatePercent,
        isPriceIncludingTax: item.taxIncluded,
        itemDiscount: { type: item.itemDiscountType as 0 | 1 | 2, value: item.itemDiscountValue },
        clientLineKey: item.clientLineKey,
        hsnCode: item.hsnCode ?? null,
      })),
    };
  }

  clearPreviewState(): void {
    this.previewRequestState.latestRequestId += 1;
    this.checkoutPreview.set(null);
    this.isPreviewLoading.set(false);
    this.previewError.set('');
  }

  clearPreviewError(): void {
    this.previewError.set('');
  }

  markPreviewInvalid(errorMessage: string): void {
    this.previewRequestState.latestRequestId += 1;
    this.checkoutPreview.set(null);
    this.isPreviewLoading.set(false);
    this.previewError.set(errorMessage);
  }

  private buildPreviewRequestStream(options: PreviewSetupOptions): Observable<PreviewRequestResult | null> {
    const cart = options.getCart();
    if (cart.length === 0) {
      this.clearPreviewState();
      return of(null);
    }
    if (!this.networkStatus.canReachApi()) {
      this.isPreviewLoading.set(false);
      return of(null);
    }
    const requestId = this.beginPreviewRequest();
    const discount = options.getSaleDiscount();
    const request = this.buildPreviewRequest(cart, discount.type as 0 | 1 | 2, discount.value);
    return this.saleService.previewSale(request).pipe(
      map((preview) => ({ requestId, preview } as PreviewRequestResult)),
      catchError((error: unknown) => of({
        requestId,
        preview: null,
        failed: true,
        errorMessage: this.extractPreviewErrorMessage(error),
      } as PreviewRequestResult)),
    );
  }
}
