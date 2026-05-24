import { NewSalePageSearchCustomerService } from './new-sale-page.search-customer.service';
import { AvailableBatchDto } from '../../inventory/services/inventory.models';
import { PAYMENT_METHOD_VALUES, PaymentMethod } from '../services/sale.models';
import {
  CartLineNumberEvent,
  CartLineTextEvent,
  CartQuantityChangedEvent,
} from '../components/new-sale/cart-table.component';

export abstract class NewSalePageCartSelectionService extends NewSalePageSearchCustomerService {
  onSelectBatch(batch: AvailableBatchDto): void {
    this.selectedBatch.set(batch);
    this.batchPickerForm.patchValue({ batchNumber: batch.batchNumber, quantity: 1 });
  }

  onCartTableQuantityChanged(event: CartQuantityChangedEvent): void {
    const index = this.cart().findIndex((item) => item.clientLineKey === event.itemId);
    if (index < 0) {
      return;
    }

    const item = this.cart()[index];
    const target = Math.max(1, Math.trunc(Number(event.qty ?? item.quantity)));
    if (!Number.isFinite(target)) {
      return;
    }

    if (target > item.quantity) {
      const canIncrease = target - item.quantity;
      for (let i = 0; i < canIncrease; i += 1) {
        this.onIncreaseCartItem(index);
      }
      return;
    }

    if (target < item.quantity) {
      const canDecrease = item.quantity - target;
      for (let i = 0; i < canDecrease; i += 1) {
        this.onDecreaseCartItem(index);
      }
    }
  }

  onCartTableItemRemoved(itemId: string): void {
    const index = this.cart().findIndex((item) => item.clientLineKey === itemId);
    if (index < 0) {
      return;
    }

    this.onRemoveCartItem(index);
  }

  onCartTableHsnCodeChange(event: CartLineTextEvent): void {
    this.onCartItemHsnCodeChange(event.itemId, event.value);
  }

  onCartTableTaxRateChange(event: CartLineNumberEvent): void {
    this.onCartItemTaxRateChange(event.itemId, event.value);
  }

  onCartTableDiscountTypeChange(event: CartLineNumberEvent): void {
    this.onCartItemDiscountTypeChange(event.itemId, event.value as 0 | 1 | 2);
  }

  onCartTableDiscountValueChange(event: CartLineTextEvent): void {
    this.onCartItemDiscountValueChange(event.itemId, Number(event.value ?? 0));
  }

  onCartTableLineDiscountEditorToggled(itemId: string): void {
    this.toggleLineDiscountEditor(itemId);
  }

  onPaymentMethodChanged(method: PaymentMethod): void {
    const nextValue = this.getPaymentMethodValue(method);
    this.paymentForm.controls.paymentMethod.setValue(nextValue, { emitEvent: true });
  }

  onPaymentPaidAmountChanged(value: number | null): void {
    this.paymentForm.controls.paidAmount.setValue(this.normalizeAmount(value, this.totalAmount()), { emitEvent: true });
  }

  onPaymentDueAmountChanged(value: number | null): void {
    this.paymentForm.controls.dueAmount.setValue(this.normalizeAmount(value, this.totalAmount()), { emitEvent: true });
  }

  onPaymentSubmitRequested(): void {
    void this.onSubmit();
  }

  onOnlineConfirmationPrintA4Requested(): void {
    const sale = this.saleConfirmationResult();
    if (!sale) {
      return;
    }

    this.printA4(sale.saleId);
  }

  onOnlineConfirmationPrintThermalRequested(): void {
    const sale = this.saleConfirmationResult();
    if (!sale) {
      return;
    }

    this.printThermal(sale.saleId);
  }

  onOfflineConfirmationPrintA4Requested(): void {
    this.printOfflineA4();
  }

  onOfflineConfirmationPrintThermalRequested(): void {
    this.printOfflineThermal();
  }

  getPaymentMethodLabel(paymentMethod: number): PaymentMethod {
    return (PAYMENT_METHOD_VALUES.find((candidate) => candidate.value === paymentMethod)?.label as PaymentMethod) ?? 'Cash';
  }

  getPaymentMethodValue(method: PaymentMethod): number {
    return PAYMENT_METHOD_VALUES.find((candidate) => candidate.label === method)?.value ?? 1;
  }

}
