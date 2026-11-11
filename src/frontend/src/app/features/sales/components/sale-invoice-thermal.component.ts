import { LocaleInstantPipe } from '../../../shared/pipes/locale-instant.pipe';
import { LocaleBusinessDatePipe } from '../../../shared/pipes/locale-business-date.pipe';
import { LocaleMoneyPipe } from '../../../shared/pipes/locale-money.pipe';
import { SaleCancellationRecordComponent } from './sale-cancellation-record.component';
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
import type {
  InvoiceDeclaration,
  InvoiceDocument,
  SaleDto,
  SaleItemDto,
} from '../services/sale.models';
import { ShopDetails } from '../../shops/services/shop.service';
import {
  type TaxSummaryRow,
  declarationKey,
  documentTitleKey,
  groupTaxComponents,
  hasDistinctTradeName,
  missingDocumentNoteKey,
  paymentStatusKey,
  redeemedCreditNoteCodes,
  paymentMethodLabelKey,
} from './sale-invoice-document.util';

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
  imports: [
    LocaleMoneyPipe,
    LocaleBusinessDatePipe,
    LocaleInstantPipe,
    SaleCancellationRecordComponent,
    CommonModule,
    TranslocoPipe,
  ],
  templateUrl: './sale-invoice-thermal.component.html',
  styleUrl: './sale-invoice-thermal.component.scss',
})
export class SaleInvoiceThermalComponent implements AfterViewInit, OnDestroy {
  private readonly transloco = inject(TranslocoService);
  private readonly elementRef = inject(ElementRef<HTMLElement>);
  private readonly renderer = inject(Renderer2);
  private pageSizeStyleElement: HTMLStyleElement | null = null;

  @Input() sale!: SaleDto;
  /** The live shop: used only for a sale without an issued document. An issued document carries its own seller. */
  @Input() shop!: ShopDetails;
  @Input() pendingSync = false;
  /** Saved document facts for a nonfiscal preview, without a posted sale. */
  @Input() sampleDocument: InvoiceDocument | null = null;

  get document(): InvoiceDocument | null {
    return this.sampleDocument ?? this.sale?.document ?? null;
  }

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
    return redeemedCreditNoteCodes(this.sale);
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

  getPaymentMethodLabel(method: number | string): string {
    return this.transloco.translate(paymentMethodLabelKey(method));
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

  getPaymentStatusKey(): string {
    return paymentStatusKey(this.sale);
  }

  getPaymentStatus(): string {
    return this.transloco.translate(this.getPaymentStatusKey());
  }

  missingDocumentNoteKey(): string | null {
    return missingDocumentNoteKey(this.sale, this.pendingSync);
  }

  documentTitleKey(document: InvoiceDocument): string {
    return documentTitleKey(document);
  }

  hasDistinctTradeName(document: InvoiceDocument): boolean {
    return hasDistinctTradeName(document);
  }

  declarationKey(declaration: InvoiceDeclaration): string {
    return declarationKey(declaration);
  }

  taxSummary(document: InvoiceDocument): TaxSummaryRow[] {
    return groupTaxComponents(document.totals.components);
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
