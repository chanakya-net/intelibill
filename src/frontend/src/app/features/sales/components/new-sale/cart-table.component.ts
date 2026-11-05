import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';

import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { FormsModule } from '@angular/forms';
import { TagModule } from 'primeng/tag';
import { InputTextModule } from 'primeng/inputtext';
import { TranslocoPipe } from '@ngneat/transloco';
import { QuantityFieldComponent } from '../../../../shared/quantity/quantity-field.component';
import { normalizeQuantity } from '../../../../shared/quantity/quantity-input';
import { QUANTITY_SCALE_MAX } from '../../../../shared/quantity/quantity-units';
import {
  CartItem,
  ServiceCartLine,
  canDecreaseCartLine,
} from '../../../../features/sales/services/sale-cart-state.service';
import { SalePreviewDto, SalePreviewLineDto } from '../../../../features/sales/services/sale.models';

export interface CartQuantityChangedEvent {
  readonly itemId: string;
  readonly qty: number;
}

export interface CartLineTextEvent {
  readonly itemId: string;
  readonly value: string;
}

export interface CartLineNumberEvent {
  readonly itemId: string;
  readonly value: number;
}

export interface InstantDiscountOption {
  readonly value: 0 | 1 | 2;
  readonly labelKey: string;
}

@Component({
  selector: 'app-cart-table',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TableModule,
    ButtonModule,
    TagModule,
    InputTextModule,
    QuantityFieldComponent,
    TranslocoPipe,
  ],
  templateUrl: './cart-table.component.html',
  styleUrl: './cart-table.component.scss',
})
export class CartTableComponent {
  @Input() cartItems: CartItem[] = [];
  @Input() serviceCartItems: ServiceCartLine[] = [];
  @Input() showGroupedSections = false;
  @Input() highlightedRowKeys: Set<string> = new Set();
  @Input() checkoutPreview: SalePreviewDto | null = null;
  @Input() saleDiscountType = 0 as 0 | 1 | 2;
  @Input() saleDiscountError = '';
  @Input() isLineDiscountEditorOpen = (_itemId: string): boolean => false;
  @Input() hasTax = (_item: CartItem) => false;
  @Input() canIncrease = (_item: CartItem) => false;
  @Input() canDecrease = (item: CartItem): boolean => canDecreaseCartLine(item);
  @Input() getLineSubtotal = (_item: CartItem) => 0;
  @Input() getLineTaxAmount = (_item: CartItem) => 0;
  @Input() getLineTotal = (_item: CartItem) => 0;
  @Input() getUnitSubtotal = (_item: CartItem) => 0;
  @Input() getUnitTaxAmount = (_item: CartItem) => 0;
  @Input() getUnitFinalPrice = (_item: CartItem) => 0;
  @Input() getServiceLineTotal = (_item: ServiceCartLine) => 0;
  @Input() getPreviewLine: (itemId: string) => SalePreviewLineDto | null = () => null;
  @Input() getCartItemHsnError = (_itemId: string) => '';
  @Input() getCartItemTaxError = (_itemId: string) => '';
  @Input() getCartItemDiscountError = (_itemId: string) => '';
  @Input() instantDiscountTypeOptions: InstantDiscountOption[] = [];
  @Input() totalAmount = 0;

  @Output() quantityChanged = new EventEmitter<CartQuantityChangedEvent>();
  @Output() itemRemoved = new EventEmitter<string>();
  @Output() serviceUnitPriceChanged = new EventEmitter<CartLineNumberEvent>();
  @Output() cartItemHsnCodeChange = new EventEmitter<CartLineTextEvent>();
  @Output() cartItemTaxRateChange = new EventEmitter<CartLineNumberEvent>();
  @Output() cartItemDiscountTypeChange = new EventEmitter<CartLineNumberEvent>();
  @Output() cartItemDiscountValueChange = new EventEmitter<CartLineTextEvent>();
  @Output() lineDiscountEditorToggled = new EventEmitter<string>();

  /**
   * A line keeps its row when its quantity or price changes, so the field being typed in, or the button tabbed to
   * after it commits, keeps focus.
   */
  readonly trackLine = (_index: number, line: { readonly clientLineKey: string }): string => line.clientLineKey;

  /** Plus is one unit more for every unit (one kilogram of a kg line). */
  increase(item: CartItem): void {
    this.quantityChanged.emit({ itemId: item.clientLineKey, qty: stepped(item.quantity + 1) });
  }

  /** Minus is one unit less, and only while at least one step of the unit would remain. */
  decrease(item: CartItem): void {
    if (this.canDecrease(item)) {
      this.quantityChanged.emit({ itemId: item.clientLineKey, qty: stepped(item.quantity - 1) });
    }
  }

  /** A quantity the line's field committed, exactly as entered. */
  setQuantity(itemId: string, qty: number): void {
    this.quantityChanged.emit({ itemId, qty });
  }

  remove(itemId: string): void {
    this.itemRemoved.emit(itemId);
  }

  emitHsnCode(itemId: string, value: string): void {
    this.cartItemHsnCodeChange.emit({ itemId, value });
  }

  emitTaxRate(itemId: string, value: number | null): void {
    const n = Number(value ?? 0);
    const normalized = Number.isFinite(n) ? Number(n.toFixed(2)) : 0;
    this.cartItemTaxRateChange.emit({ itemId, value: normalized });
  }

  emitDiscountType(itemId: string, value: number | null): void {
    this.cartItemDiscountTypeChange.emit({ itemId, value: Number(value ?? 0) as 0 | 1 | 2 });
  }

  emitDiscountValue(itemId: string, value: number | null): void {
    const n = Number(value ?? 0);
    const normalized = Number.isFinite(n) ? Number(n.toFixed(2)) : 0;
    this.cartItemDiscountValueChange.emit({ itemId, value: normalized.toString() });
  }

  toggleLineDiscountEditor(itemId: string): void {
    this.lineDiscountEditorToggled.emit(itemId);
  }

  emitServiceUnitPrice(itemId: string, value: number | null): void {
    const n = Number(value ?? 0);
    const normalized = Number.isFinite(n) ? Number(n.toFixed(2)) : 0;
    this.serviceUnitPriceChanged.emit({ itemId, value: normalized });
  }
}

/** One unit more or less of a stored quantity, without binary float noise (1.1 - 1 is 0.1). */
function stepped(value: number): number {
  return normalizeQuantity(value, QUANTITY_SCALE_MAX);
}
