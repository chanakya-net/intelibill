import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { DividerModule } from 'primeng/divider';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TagModule } from 'primeng/tag';
import { TableModule } from 'primeng/table';

import { SalesFacade } from '../state/sales.facade';

@Component({
  selector: 'app-sale-detail-overlay',
  standalone: true,
  imports: [
    CommonModule,
    ButtonModule,
    DialogModule,
    DividerModule,
    ProgressSpinnerModule,
    TagModule,
    TableModule,
    TranslocoPipe,
  ],
  template: `
    <p-dialog
      [visible]="visible"
      (visibleChange)="onClose()"
      [modal]="true"
      [style]="{ width: '90vw', maxWidth: '640px' }"
      [draggable]="false"
      [resizable]="false"
      [closeOnEscape]="true"
      [header]="'sales.detail.title' | transloco"
    >
      @if (isLoading()) {
        <div class="flex justify-center py-8">
          <p-progressSpinner styleClass="h-8 w-8" strokeWidth="6" />
        </div>
      }

      @if (!isLoading() && sale()) {
        <div class="flex flex-col gap-4">
          <div class="grid grid-cols-2 gap-3 text-sm">
            <div>
              <p class="text-xs text-slate-400 uppercase tracking-wider font-semibold mb-1">{{ 'sales.detail.invoiceNumber' | transloco }}</p>
              <p class="font-bold text-slate-800">{{ sale()!.invoiceNumber }}</p>
            </div>
            <div>
              <p class="text-xs text-slate-400 uppercase tracking-wider font-semibold mb-1">{{ 'sales.detail.date' | transloco }}</p>
              <p class="text-slate-700">{{ sale()!.soldAt | date:'dd MMM yyyy, h:mm a' }}</p>
            </div>
            <div>
              <p class="text-xs text-slate-400 uppercase tracking-wider font-semibold mb-1">{{ 'sales.detail.payment' | transloco }}</p>
              <p-tag [severity]="paymentMethodSeverity(sale()!.paymentMethod)" [value]="paymentMethodLabel(sale()!.paymentMethod)" />
            </div>
            <div>
              <p class="text-xs text-slate-400 uppercase tracking-wider font-semibold mb-1">{{ 'sales.detail.total' | transloco }}</p>
              <p class="font-bold text-orange-600 text-base">₹{{ sale()!.totalAmount | number:'1.2-2' }}</p>
            </div>
          </div>

          <p-divider />

          <p-table [value]="$any(sale()!.items)" styleClass="p-datatable-sm">
            <ng-template pTemplate="header">
              <tr>
                <th>{{ 'sales.detail.qty' | transloco }}</th>
                <th>{{ 'sales.detail.price' | transloco }}</th>
                <th>{{ 'sales.detail.tax' | transloco }}</th>
              </tr>
            </ng-template>
            <ng-template pTemplate="body" let-item>
              <tr>
                <td>{{ item.quantity }}</td>
                <td>₹{{ item.salesPrice | number:'1.2-2' }}</td>
                <td>{{ item.taxRatePercent }}%</td>
              </tr>
            </ng-template>
          </p-table>

          <div class="flex justify-between items-center pt-2 border-t border-slate-100">
            <span class="text-sm text-slate-500">{{ 'sales.detail.taxAmount' | transloco }}</span>
            <span class="font-medium text-slate-700">₹{{ sale()!.totalTaxAmount | number:'1.2-2' }}</span>
          </div>
        </div>
      }

      <ng-template pTemplate="footer">
        <p-button
          [label]="'common.close' | transloco"
          severity="secondary"
          (click)="onClose()"
        />
      </ng-template>
    </p-dialog>
  `,
})
export class SaleDetailOverlayComponent {
  private readonly salesFacade = inject(SalesFacade);

  @Input() visible = false;
  @Output() visibleChange = new EventEmitter<boolean>();

  readonly sale = this.salesFacade.selectedSale;
  readonly isLoading = this.salesFacade.loadingSaleDetail;

  onClose(): void {
    this.visibleChange.emit(false);
  }

  paymentMethodLabel(method: number): string {
    const map: Record<number, string> = { 1: 'Cash', 2: 'UPI', 3: 'Card', 4: 'Credit' };
    return map[method] ?? 'Unknown';
  }

  paymentMethodSeverity(method: number): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    const map: Record<number, 'success' | 'info' | 'warn' | 'danger'> = { 1: 'success', 2: 'info', 3: 'warn', 4: 'danger' };
    return map[method] ?? 'secondary';
  }
}
