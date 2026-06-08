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
      <section class="directory-panel receipt-panel">
        <header class="directory-panel__header">
          <div>
            <p class="directory-panel__eyebrow">{{ 'purchaseOrders.receipts.title' | transloco }}</p>
            <h2>{{ 'purchaseOrders.receipts.title' | transloco }}</h2>
          </div>
          <div class="directory-panel__metrics" aria-label="Receipt count">
            <span class="metric-chip">
              <strong>{{ receipts.length }}</strong>
              <span>{{ 'purchaseOrders.receipts.title' | transloco }}</span>
            </span>
          </div>
        </header>

        @for (receipt of receipts; track receipt.receiptId) {
          <article class="receipt-block">
            <div class="receipt-header">
              <strong class="receipt-number">{{ receipt.receiptNumber }}</strong>
              <span class="receipt-meta">{{ receipt.receivedAt | date:'medium' }}</span>
              @if (receipt.referenceNumber) {
                <span class="receipt-meta">{{ receipt.referenceNumber }}</span>
              }
              @if (receipt.receivedByDisplayName) {
                <span class="receipt-meta">{{ receipt.receivedByDisplayName }}</span>
              }
            </div>
            <div class="directory-panel__surface">
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
                    <td class="money">{{ line.unitCost | number:'1.2-2' }}</td>
                    <td class="money">{{ line.totalPurchaseCost | number:'1.2-2' }}</td>
                    <td>{{ line.salesPrice | number:'1.2-2' }} / {{ line.mrp | number:'1.2-2' }}</td>
                    <td>{{ line.taxRatePercent | number:'1.0-2' }}%</td>
                    <td class="receipt-transaction">{{ line.stockTransactionId }}</td>
                  </tr>
                </ng-template>
              </p-table>
            </div>
          </article>
        }
      </section>
    }
  `,
  styles: [`
    :host { display: block; }
    .directory-panel {
      display: flex;
      flex-direction: column;
      gap: 0.65rem;
      padding: 0.75rem;
      border: 1px solid #ead7c1;
      border-radius: 1.2rem;
      background: rgba(255, 251, 246, 0.92);
      box-shadow: 0 10px 30px rgba(78, 49, 20, 0.07);
    }
    .directory-panel__header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: 0.75rem;
      flex-wrap: wrap;
    }
    .directory-panel__eyebrow {
      margin: 0;
      color: #9b5d20;
      font-size: 0.68rem;
      font-weight: 800;
      letter-spacing: 0.16em;
      text-transform: uppercase;
    }
    .directory-panel__header h2 {
      margin: 0.2rem 0 0;
      color: #271911;
      font-size: 1.2rem;
      font-weight: 800;
    }
    .directory-panel__metrics {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: 0.45rem;
    }
    .metric-chip {
      display: inline-flex;
      flex-direction: column;
      gap: 0.12rem;
      min-width: 8rem;
      padding: 0.45rem 0.65rem;
      border: 1px solid #ead7c1;
      border-radius: 0.75rem;
      background: linear-gradient(180deg, #fffaf3, #fff2e5);
    }
    .metric-chip strong {
      color: #2a1b12;
      font-size: 0.92rem;
      font-weight: 800;
      line-height: 1;
    }
    .metric-chip span {
      color: #8b6e57;
      font-size: 0.72rem;
      font-weight: 700;
    }
    .receipt-block {
      display: grid;
      gap: 0.55rem;
    }
    .receipt-header {
      display: flex;
      gap: 0.75rem;
      align-items: center;
      flex-wrap: wrap;
      padding: 0 0.15rem;
    }
    .receipt-number {
      color: #2a1b12;
      font-size: 0.95rem;
    }
    .receipt-meta {
      color: #8b6e57;
      font-size: 0.82rem;
      font-weight: 600;
    }
    .directory-panel__surface {
      overflow: hidden;
      border: 1px solid #eedcc8;
      border-radius: 0.9rem;
      background: #fffaf4;
    }
    .money {
      font-weight: 800;
      color: #2a1b12;
    }
    .receipt-transaction {
      font-size: 0.78rem;
      color: #6f5a48;
      overflow-wrap: anywhere;
    }
    a {
      color: #c2410c;
      font-weight: 700;
      text-decoration: none;
    }
    :host ::ng-deep .directory-panel__surface .p-datatable-table {
      border-collapse: collapse;
    }
    :host ::ng-deep .directory-panel__surface .p-datatable-thead > tr > th {
      border-color: #ead7c1;
      background: #fff5e7;
      color: #7a6250;
      font-size: 0.68rem;
      font-weight: 800;
      letter-spacing: 0.14em;
      text-transform: uppercase;
      padding: 0.55rem 0.8rem;
    }
    :host ::ng-deep .directory-panel__surface .p-datatable-tbody > tr > td {
      border-color: #ead7c1;
      color: #34281f;
      padding: 0.55rem 0.8rem;
    }
  `],
})
export class PurchaseOrderReceiptHistoryComponent {
  @Input() receipts: readonly PurchaseOrderReceipt[] = [];
}
