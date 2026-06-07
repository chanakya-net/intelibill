import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';

import type { CreatePurchaseOrderLineRequest } from '../services/purchase-order.service';

@Component({
  selector: 'app-purchase-order-lines-table',
  standalone: true,
  imports: [CommonModule],
  template: `
    <table class="po-lines-table">
      <thead>
        <tr>
          <th>Item</th>
          <th>Qty</th>
          <th>Unit cost</th>
          <th>Total</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        @for (line of lines; track line.description) {
          <tr>
            <td>{{ line.description }}</td>
            <td>{{ line.expectedQuantity }}</td>
            <td>{{ line.unitCost | number:'1.2-2' }}</td>
            <td>{{ line.expectedQuantity * line.unitCost | number:'1.2-2' }}</td>
            <td><button type="button" (click)="removeLine.emit(line.description)">Remove</button></td>
          </tr>
        } @empty {
          <tr><td colspan="5">No lines</td></tr>
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
