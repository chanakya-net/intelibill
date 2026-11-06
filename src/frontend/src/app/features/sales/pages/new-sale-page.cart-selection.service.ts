import { NewSalePageSearchCustomerService } from './new-sale-page.search-customer.service';
import { QUANTITY_REFUSAL_MESSAGE_KEYS, QuantityRefusalLine } from './new-sale-page.state.service';
import { AvailableBatchDto } from '../../inventory/services/inventory.models';
import { PAYMENT_METHOD_VALUES, PaymentMethod } from '../services/sale.models';
import type { QuantityChangeResult } from '../services/sale-cart-state.service';
import {
  CartLineNumberEvent,
  CartLineTextEvent,
  CartQuantityChangedEvent,
} from '../components/new-sale/cart-table.component';
import { batchToGoodsSellable } from '../utils/sellable-goods.mapper';

export abstract class NewSalePageCartSelectionService extends NewSalePageSearchCustomerService {
  onSelectBatch(batch: AvailableBatchDto): void {
    this.selectedSellable.set(batchToGoodsSellable(batch));
    this.batchPickerForm.patchValue({ batchNumber: batch.batchNumber, quantity: 1 });
  }

  /** The cart table asks for an exact quantity (typed, or one unit more or less): one change, or a refusal. */
  onCartTableQuantityChanged(event: CartQuantityChangedEvent): void {
    if (!this.cart().some((item) => item.clientLineKey === event.itemId)) {
      this.onServiceItemQuantityChange(event.itemId, event.qty);
      return;
    }

    this.reportQuantityChange(
      this.cartState.setQuantity(event.itemId, event.qty),
      this.cart().find((item) => item.clientLineKey === event.itemId),
    );
  }

  /** Shows why a quantity was refused (the line is unchanged); a change that lands clears that message. */
  protected reportQuantityChange(result: QuantityChangeResult, line?: QuantityRefusalLine | null): void {
    if (result === 'ok' || result === 'unchanged') {
      if (QUANTITY_REFUSAL_MESSAGE_KEYS.has(this.batchSearchError())) {
        this.batchSearchError.set('');
        this.batchSearchErrorParams.set(null);
      }
      return;
    }

    if (result !== 'notFound') {
      this.showQuantityRefusal(result, line);
    }
  }

  onCartTableItemRemoved(itemId: string): void {
    const index = this.cart().findIndex((item) => item.clientLineKey === itemId);
    if (index < 0) {
      this.onRemoveServiceItem(itemId);
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

  onCartTableServiceUnitPriceChanged(event: CartLineNumberEvent): void {
    this.onServiceItemPriceChange(event.itemId, event.value);
  }

  onPaymentMethodChanged(method: PaymentMethod): void {
    const nextValue = this.getPaymentMethodValue(method);
    this.paymentForm.controls.paymentMethod.setValue(nextValue, { emitEvent: true });
  }

  onPaymentPaidAmountChanged(value: number | null): void {
    this.paymentForm.controls.paidAmount.setValue(this.normalizeAmount(value, this.getPayableAmount()), { emitEvent: true });
  }

  onPaymentDueAmountChanged(value: number | null): void {
    this.paymentForm.controls.dueAmount.setValue(this.normalizeAmount(value, this.getPayableAmount()), { emitEvent: true });
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
