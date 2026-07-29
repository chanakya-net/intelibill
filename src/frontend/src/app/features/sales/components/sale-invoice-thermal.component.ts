import { CommonModule } from '@angular/common';
import {
  AfterViewInit,
  Component,
  ElementRef,
  Input,
  OnDestroy,
  Renderer2,
  inject,
} from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';

import type { SaleDto, SaleItemDto } from '../services/sale.models';
import { ShopDetails } from '../../shops/services/shop.service';

const THERMAL_PAGE_WIDTH_MM = 80;
// A receipt shorter than this fits on one content-sized page with no blank
// tail. Longer content falls back to this height per page so Chromium keeps
// pagination behavior for long receipts instead of forcing one giant page.
const THERMAL_MAX_SINGLE_PAGE_HEIGHT_MM = 297;
// Rounds up for sub-pixel layout and print-engine rounding.
const THERMAL_PAGE_HEIGHT_BUFFER_MM = 4;
const PX_TO_MM = 25.4 / 96;

@Component({
  selector: 'app-sale-invoice-thermal',
  standalone: true,
  imports: [CommonModule, TranslocoPipe],
  templateUrl: './sale-invoice-thermal.component.html',
  styleUrl: './sale-invoice-thermal.component.scss',
})
export class SaleInvoiceThermalComponent implements AfterViewInit, OnDestroy {
  private readonly transloco = inject(TranslocoService);
  private readonly elementRef = inject(ElementRef<HTMLElement>);
  private readonly renderer = inject(Renderer2);
  private pageSizeStyleElement: HTMLStyleElement | null = null;

  @Input() sale!: SaleDto;
  @Input() shop!: ShopDetails;
  @Input() pendingSync = false;

  ngAfterViewInit(): void {
    this.applyContentFittedPageSize();
  }

  ngOnDestroy(): void {
    this.pageSizeStyleElement?.remove();
    this.pageSizeStyleElement = null;
  }

  private applyContentFittedPageSize(): void {
    const contentHeightMm = this.elementRef.nativeElement.scrollHeight * PX_TO_MM;
    const pageHeightMm = Math.min(
      Math.ceil(contentHeightMm) + THERMAL_PAGE_HEIGHT_BUFFER_MM,
      THERMAL_MAX_SINGLE_PAGE_HEIGHT_MM,
    );

    const style = this.renderer.createElement('style') as HTMLStyleElement;
    style.textContent = `@page { size: ${THERMAL_PAGE_WIDTH_MM}mm ${pageHeightMm}mm; margin: 0; }`;
    this.renderer.appendChild(document.head, style);
    this.pageSizeStyleElement = style;
  }

  getCreditNoteSettlementCodes(): string[] {
    return this.sale.returns
      .map((saleReturn) => saleReturn.creditNote?.code ?? '')
      .filter((code): code is string => code.trim().length > 0);
  }

  getCreditNoteSettlementLabelKey(): string {
    const codes = this.getCreditNoteSettlementCodes();

    if (codes.length === 0) {
      return 'sales.invoice.creditNoteSettlement';
    }

    return 'sales.invoice.creditNoteSettlementWithCodes';
  }

  get shopAddress(): string {
    return [this.shop.address, this.shop.city, this.shop.state, this.shop.pincode]
      .filter(Boolean)
      .join(', ');
  }

  getPaymentMethodLabel(method: number): string {
    const map: Record<number, string> = {
      1: 'sales.newSale.paymentMethods.cash',
      2: 'sales.newSale.paymentMethods.upi',
      3: 'sales.newSale.paymentMethods.card',
      4: 'sales.newSale.paymentMethods.credit',
    };
    return this.transloco.translate(map[method] ?? 'shops.unknown');
  }

  getCustomerDisplay(): string {
    if (this.sale.customerName) {
      return this.sale.customerName;
    }

    return this.transloco.translate('sales.history.walkIn');
  }

  getCustomerPhone(): string | null {
    return this.sale.customerPhone;
  }

  getLineDiscountAmount(item: SaleItemDto): number {
    return item.itemDiscountAmount + item.saleDiscountAmount;
  }

  getPaymentStatus(): string {
    if (this.sale.dueAmount === 0) {
      return this.transloco.translate('sales.invoice.paid');
    }

    if (this.sale.paidAmount > 0) {
      return this.transloco.translate('sales.invoice.partiallyPaid');
    }

    return this.transloco.translate('sales.invoice.unpaid');
  }

  hasGoods(): boolean {
    return this.sale.items.some((i) => i.lineType === 'Goods');
  }

  hasServices(): boolean {
    return this.sale.items.some((i) => i.lineType === 'Service');
  }

  isMixedBill(): boolean {
    return this.hasGoods() && this.hasServices();
  }

  getGoodsItems(): SaleItemDto[] {
    return this.sale.items.filter((i) => i.lineType === 'Goods');
  }

  getServiceItems(): SaleItemDto[] {
    return this.sale.items.filter((i) => i.lineType === 'Service');
  }
}
