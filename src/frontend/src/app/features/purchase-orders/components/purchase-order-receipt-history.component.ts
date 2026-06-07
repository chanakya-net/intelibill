import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';

import { PurchaseOrderReceipt } from '../services/purchase-order.service';

@Component({
  selector: 'app-purchase-order-receipt-history',
  standalone: true,
  imports: [CommonModule, RouterLink, TranslocoPipe, TableModule, TagModule],
  template: `
    @if (receipts.length > 0) {
      <h3>{{ 'purchaseOrders.receipts.title' | transloco }}</h3>
      @for (receipt of receipts; track receipt.receiptId) {
        <section class="receipt-block">
          <div class="receipt-header">
            <strong>{{ receipt.receiptNumber }}</strong>
            <span>{{ receipt.receivedAt }}</span>
            @if (receipt.referenceNumber) {
              <span>{{ receipt.referenceNumber }}</span>
            }
            @if (receipt.receivedByDisplayName) {
              <span>{{ receipt.receivedByDisplayName }}</span>
            }
          </div>
          <p-table [value]="[...receipt.lines]" dataKey="receiptLineId">
            <ng-template pTemplate="header">
              <tr>
                <th>{{ 'purchaseOrders.receipts.batch' | transloco }}</th>
                <th>{{ 'purchaseOrders.receipts.quantity' | transloco }}</th>
                <th>{{ 'purchaseOrders.receipts.unitCost' | transloco }}</th>
                <th>{{ 'purchaseOrders.receipts.totalCost' | transloco }}</th>
                <th>{{ 'purchaseOrders.receipts.pricing' | transloco }}</th>
                <th>{{ 'purchaseOrders.receipts.tax' | transloco }}</th>
                <th>{{ 'purchaseOrders.receipts.stockTransaction' | transloco }}</th>
              </tr>
            </ng-template>
            <ng-template pTemplate="body" let-line>
              <tr>
                <td>
                  <a [routerLink]="['/inventory/batches']" [queryParams]="{ batchId: line.inventoryBatchId }">
                    {{ line.batchNumber || line.inventoryBatchId }}
                  </a>
                  @if (line.batchVoided) {
                    <p-tag [value]="'purchaseOrders.receipts.voided' | transloco" severity="danger" />
                  }
                </td>
                <td>{{ line.quantity }}</td>
                <td>{{ line.unitCost | number:'1.2-2' }}</td>
                <td>{{ line.totalPurchaseCost | number:'1.2-2' }}</td>
                <td>{{ line.salesPrice | number:'1.2-2' }} / {{ line.mrp | number:'1.2-2' }}</td>
                <td>{{ line.taxRatePercent | number:'1.0-2' }}%</td>
                <td>{{ line.stockTransactionId }}</td>
              </tr>
            </ng-template>
          </p-table>
        </section>
      }
    }
  `,
  styles: [`
    .receipt-block { display: grid; gap: .5rem; margin-top: .75rem; }
    .receipt-header { display: flex; gap: 1rem; align-items: center; flex-wrap: wrap; }
    a { color: var(--p-primary-color); text-decoration: none; }
  `],
})
export class PurchaseOrderReceiptHistoryComponent {
  @Input() receipts: readonly PurchaseOrderReceipt[] = [];
}
