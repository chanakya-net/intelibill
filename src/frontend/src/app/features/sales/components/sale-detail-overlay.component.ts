import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TagModule } from 'primeng/tag';
import { TableModule } from 'primeng/table';
import { AuthService } from '../../../core/auth/auth.service';
import { getPaymentMethodLabel, type SaleReturnDto } from '../services/sale.models';
import { SalesFacade } from '../state/sales.facade';
import { SaleLineItemsTableComponent } from './sale-detail/sale-line-items-table.component';
import { SaleReturnPreviewDialogComponent } from './sale-detail/sale-return-preview-dialog.component';
import { SaleSummaryPanelComponent } from './sale-detail/sale-summary-panel.component';
import { SaleVoidReturnDialogComponent } from './sale-detail/sale-void-return-dialog.component';

@Component({
  selector: 'app-sale-detail-overlay',
  standalone: true,
  imports: [
    CommonModule,
    ButtonModule,
    DialogModule,
    ProgressSpinnerModule,
    TagModule,
    TableModule,
    TranslocoPipe,
    SaleLineItemsTableComponent,
    SaleReturnPreviewDialogComponent,
    SaleSummaryPanelComponent,
    SaleVoidReturnDialogComponent,
  ],
  templateUrl: './sale-detail-overlay.component.html',
  styleUrl: './sale-detail-overlay.component.scss',
})
export class SaleDetailOverlayComponent {
  private readonly salesFacade = inject(SalesFacade);
  private readonly authService = inject(AuthService);
  private readonly transloco = inject(TranslocoService);
  private readonly activeLanguage = toSignal(this.transloco.langChanges$, {
    initialValue: this.transloco.getActiveLang(),
  });

  @Input() visible = false;
  @Output() readonly visibleChange = new EventEmitter<boolean>();

  readonly sale = this.salesFacade.selectedSale;
  readonly isLoading = this.salesFacade.loadingSaleDetail;

  readonly showReturnPreview = signal(false);
  readonly showVoidReturn = signal(false);
  readonly selectedReturnToVoid = signal<SaleReturnDto | null>(null);

  readonly activeShopRole = computed(() => {
    const session = this.authService.session();
    if (!session) return '';

    const activeShop =
      session.shops.find((shop) => shop.shopId === session.activeShopId) ??
      session.shops.find((shop) => shop.isDefault);

    return activeShop?.role ?? '';
  });

  readonly returnableItems = computed(
    () => this.sale()?.items.filter((item) => item.returnableQuantity > 0) ?? [],
  );

  readonly paymentMethodLabel = computed(() => {
    this.activeLanguage();
    return this.transloco.translate(this.paymentMethodLabelKey(this.sale()?.paymentMethod ?? 0));
  });

  readonly customerName = computed(() => {
    this.activeLanguage();
    return this.sale()?.customerName || this.transloco.translate('sales.invoice.walkInCustomer');
  });

  readonly customerPhone = computed(() => {
    this.activeLanguage();
    return this.sale()?.customerPhone || this.transloco.translate('sales.detail.notProvided');
  });

  readonly canPreviewReturns = computed(() => {
    const role = this.activeShopRole().toLowerCase();
    return ['owner', 'manager', 'staff'].includes(role) && this.returnableItems().length > 0;
  });

  readonly canSubmitReturns = computed(() =>
    ['owner', 'manager'].includes(this.activeShopRole().toLowerCase()),
  );

  openReturnPreview(): void {
    if (!this.sale() || !this.canPreviewReturns()) return;
    this.showReturnPreview.set(true);
  }

  closeReturnPreview(): void {
    this.showReturnPreview.set(false);
  }

  openVoidReturn(saleReturn: SaleReturnDto): void {
    if (!this.canSubmitReturns() || saleReturn.isVoided) return;

    this.selectedReturnToVoid.set(saleReturn);
    this.showVoidReturn.set(true);
  }

  closeVoidReturn(): void {
    this.showVoidReturn.set(false);
    this.selectedReturnToVoid.set(null);
  }

  printA4(): void {
    const saleId = this.sale()?.saleId;
    if (!saleId) return;

    window.open(`/sales/${saleId}/print?template=a4`, '_blank');
  }

  printThermal(): void {
    const saleId = this.sale()?.saleId;
    if (!saleId) return;

    window.open(`/sales/${saleId}/print?template=thermal`, '_blank');
  }

  onClose(): void {
    this.closeReturnPreview();
    this.closeVoidReturn();
    this.visibleChange.emit(false);
  }

  returnStatusLabel(saleReturn: SaleReturnDto): string {
    return this.transloco.translate(saleReturn.isVoided ? 'sales.invoice.voided' : 'common.active');
  }

  returnStatusSeverity(
    saleReturn: SaleReturnDto,
  ): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    return saleReturn.isVoided ? 'secondary' : 'success';
  }

  returnFooterNote(): string {
    if (this.canPreviewReturns()) {
      return this.transloco.translate('sales.detail.returnFooterAvailable');
    }

    return this.transloco.translate('sales.detail.returnFooterUnavailable');
  }

  private paymentMethodLabelKey(method: number): string {
    const label = getPaymentMethodLabel(method).toLowerCase();
    return ['cash', 'upi', 'card', 'credit'].includes(label)
      ? `sales.newSale.paymentMethods.${label}`
      : 'shops.unknown';
  }
}
