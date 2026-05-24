import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { AuthService } from '../../../../core/auth/auth.service';
import type { SaleDto, SaleReturnDto } from '../../services/sale.models';
import { SalesFacade } from '../../state/sales.facade';

@Component({
  selector: 'app-sale-void-return-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    DialogModule,
    InputTextModule,
    TextareaModule,
    TranslocoPipe,
  ],
  templateUrl: './sale-void-return-dialog.component.html',
})
export class SaleVoidReturnDialogComponent {
  private readonly salesFacade = inject(SalesFacade);
  private readonly authService = inject(AuthService);

  readonly isVisible = signal(false);

  @Input()
  set visible(value: boolean) {
    this.isVisible.set(value);
  }
  get visible(): boolean {
    return this.isVisible();
  }
  @Output() readonly visibleChange = new EventEmitter<boolean>();

  readonly saleInput = signal<SaleDto | null>(null);

  @Input()
  set sale(value: SaleDto | null) {
    this.saleInput.set(value);
  }
  get sale(): SaleDto | null {
    return this.saleInput();
  }

  readonly saleReturnInput = signal<SaleReturnDto | null>(null);

  @Input()
  set saleReturn(value: SaleReturnDto | null) {
    this.saleReturnInput.set(value);
  }
  get saleReturn(): SaleReturnDto | null {
    return this.saleReturnInput();
  }

  readonly isSubmitting = this.salesFacade.submitting;
  readonly previewError = this.salesFacade.returnPreviewErrorMessage;
  readonly validationMessages = signal<readonly string[]>([]);
  readonly voidReason = signal('');

  readonly activeShopRole = computed(() => {
    const session = this.authService.session();
    if (!session) return '';

    const activeShop =
      session.shops.find((shop) => shop.shopId === session.activeShopId) ??
      session.shops.find((shop) => shop.isDefault);

    return activeShop?.role ?? '';
  });

  readonly canSubmitReturns = computed(() => ['owner', 'manager'].includes(this.activeShopRole().toLowerCase()));

  constructor() {
    effect(() => {
      if (!this.isVisible()) return;
      if (!this.salesFacade.lastMutationSucceeded()) return;
      if (this.salesFacade.lastMutationType() !== 'void-return') return;

      this.close();
      this.salesFacade.clearMutationStatus();
    });
  }

  close(): void {
    this.visibleChange.emit(false);
    this.voidReason.set('');
    this.validationMessages.set([]);
  }

  updateVoidReason(reason: string): void {
    this.voidReason.set(reason);
  }

  submit(): void {
    const sale = this.saleInput();
    const saleReturn = this.saleReturnInput();
    if (!sale || !saleReturn) return;

    if (!this.canSubmitReturns() || saleReturn.isVoided) return;

    const reason = this.normalizeOptional(this.voidReason());
    if (!reason) {
      this.validationMessages.set(['Void reason is required.']);
      return;
    }

    this.salesFacade.voidSaleReturn(sale.saleId, saleReturn.saleReturnId, { reason });
  }

  private normalizeOptional(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }
}
