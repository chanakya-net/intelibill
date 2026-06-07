import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';

import type { CreatePurchaseOrderLineRequest } from '../services/purchase-order.service';

@Component({
  selector: 'app-purchase-order-lines-table',
  standalone: true,
  imports: [CommonModule, TranslocoPipe],
  template: `
    <table class="po-lines-table">
      <thead>
        <tr>
          <th>{{ 'purchaseOrders.builder.item' | transloco }}</th>
          <th>{{ 'purchaseOrders.builder.qty' | transloco }}</th>
          <th>{{ 'purchaseOrders.builder.unitCost' | transloco }}</th>
          <th>{{ 'purchaseOrders.builder.total' | transloco }}</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        @for (line of lines; track line.itemId) {
          <tr>
            <td>{{ line.description }}</td>
            <td>{{ line.expectedQuantity }}</td>
            <td>{{ line.unitCost | number:'1.2-2' }}</td>
            <td>{{ line.expectedQuantity * line.unitCost | number:'1.2-2' }}</td>
            <td><button type="button" (click)="removeLine.emit(line.itemId)">{{ 'purchaseOrders.builder.remove' | transloco }}</button></td>
          </tr>
        } @empty {
          <tr><td colspan="5">{{ 'purchaseOrders.builder.noLines' | transloco }}</td></tr>
        }
      </tbody>
    </table>
  `,
  styles: [`
    .po-lines-table { width: 100%; border-collapse: collapse; }
    th, td { border-bottom: 1px solid #d8dee4; padding: .65rem; text-align: left; }
    th:nth-child(2), th:nth-child(3), th:nth-child(4), td:nth-child(2), td:nth-child(3), td:nth-child(4) { text-align: right; }
  `],
})
export class PurchaseOrderLinesTableComponent {
  @Input({ required: true }) lines: readonly CreatePurchaseOrderLineRequest[] = [];
  @Output() readonly removeLine = new EventEmitter<string>();
}
