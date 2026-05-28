import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';

import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { FormsModule } from '@angular/forms';
import { TagModule } from 'primeng/tag';
import { InputTextModule } from 'primeng/inputtext';
import { TranslocoPipe } from '@ngneat/transloco';
import { CartItem } from '../../../../features/sales/services/sale-cart-state.service';
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
    TranslocoPipe,
  ],
  templateUrl: './cart-table.component.html',
  styleUrl: './cart-table.component.scss',
})
export class CartTableComponent {
  @Input() cartItems: CartItem[] = [];
  @Input() highlightedRowKeys: Set<string> = new Set();
  @Input() checkoutPreview: SalePreviewDto | null = null;
  @Input() saleDiscountType = 0 as 0 | 1 | 2;
  @Input() saleDiscountError = '';
  @Input() isLineDiscountEditorOpen = (_itemId: string): boolean => false;
  @Input() hasTax = (_item: CartItem) => false;
  @Input() canIncrease = (_item: CartItem) => false;
  @Input() getLineSubtotal = (_item: CartItem) => 0;
  @Input() getLineTaxAmount = (_item: CartItem) => 0;
  @Input() getLineTotal = (_item: CartItem) => 0;
  @Input() getUnitSubtotal = (_item: CartItem) => 0;
  @Input() getUnitTaxAmount = (_item: CartItem) => 0;
  @Input() getUnitFinalPrice = (_item: CartItem) => 0;
  @Input() getPreviewLine: (itemId: string) => SalePreviewLineDto | null = () => null;
  @Input() getCartItemHsnError = (_itemId: string) => '';
  @Input() getCartItemTaxError = (_itemId: string) => '';
  @Input() getCartItemDiscountError = (_itemId: string) => '';
  @Input() instantDiscountTypeOptions: InstantDiscountOption[] = [];
  @Input() totalAmount = 0;

  @Output() quantityChanged = new EventEmitter<CartQuantityChangedEvent>();
  @Output() itemRemoved = new EventEmitter<string>();
  @Output() cartItemHsnCodeChange = new EventEmitter<CartLineTextEvent>();
  @Output() cartItemTaxRateChange = new EventEmitter<CartLineNumberEvent>();
  @Output() cartItemDiscountTypeChange = new EventEmitter<CartLineNumberEvent>();
  @Output() cartItemDiscountValueChange = new EventEmitter<CartLineTextEvent>();
  @Output() lineDiscountEditorToggled = new EventEmitter<string>();

  increase(itemId: string, currentQty: number): void {
    this.quantityChanged.emit({ itemId, qty: currentQty + 1 });
  }

  decrease(itemId: string, currentQty: number): void {
    this.quantityChanged.emit({ itemId, qty: Math.max(1, currentQty - 1) });
  }

  remove(itemId: string): void {
    this.itemRemoved.emit(itemId);
  }

  emitHsnCode(itemId: string, value: string): void {
    this.cartItemHsnCodeChange.emit({ itemId, value });
  }

  emitTaxRate(itemId: string, value: number | null): void {
    const n = Number(value ?? 0);
    this.cartItemTaxRateChange.emit({ itemId, value: Math.round(n * 100) / 100 });
  }

  emitDiscountType(itemId: string, value: number | null): void {
    this.cartItemDiscountTypeChange.emit({ itemId, value: Number(value ?? 0) as 0 | 1 | 2 });
  }

  emitDiscountValue(itemId: string, value: number | null): void {
    const n = Number(value ?? 0);
    this.cartItemDiscountValueChange.emit({ itemId, value: (Math.round(n * 100) / 100).toString() });
  }

  toggleLineDiscountEditor(itemId: string): void {
    this.lineDiscountEditorToggled.emit(itemId);
  }
}
