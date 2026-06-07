import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { TableModule } from 'primeng/table';

import type { CreatePurchaseOrderLineRequest } from '../services/purchase-order.service';

@Component({
  selector: 'app-purchase-order-lines-table',
  standalone: true,
  imports: [CommonModule, TranslocoPipe, ButtonModule, TableModule],
  template: `
    <p-table
      [value]="$any(lines)"
      dataKey="itemId"
      styleClass="po-lines-table p-datatable-sm"
    >
      <ng-template pTemplate="header">
        <tr>
          <th>{{ 'purchaseOrders.builder.item' | transloco }}</th>
          <th class="numeric-column">{{ 'purchaseOrders.builder.qty' | transloco }}</th>
          <th class="numeric-column">{{ 'purchaseOrders.builder.unitCost' | transloco }}</th>
          <th class="numeric-column">{{ 'purchaseOrders.builder.total' | transloco }}</th>
          <th class="actions-column"></th>
        </tr>
      </ng-template>
      <ng-template pTemplate="body" let-line>
        <tr>
          <td>{{ line.description }}</td>
          <td class="numeric-column">{{ line.expectedQuantity }}</td>
          <td class="numeric-column">{{ line.unitCost | number:'1.2-2' }}</td>
          <td class="numeric-column">{{ line.expectedQuantity * line.unitCost | number:'1.2-2' }}</td>
          <td class="actions-column">
            <button
              pButton
              type="button"
              severity="danger"
              text="true"
              icon="pi pi-trash"
              [attr.aria-label]="'purchaseOrders.builder.remove' | transloco"
              (click)="removeLine.emit(line.itemId)"
            ></button>
          </td>
        </tr>
      </ng-template>
      <ng-template pTemplate="emptymessage">
        <tr>
          <td colspan="5">{{ 'purchaseOrders.builder.noLines' | transloco }}</td>
        </tr>
      </ng-template>
    </p-table>
  `,
  styles: [`
    .numeric-column { text-align: right; }
    .actions-column { width: 3rem; text-align: right; }
  `],
})
export class PurchaseOrderLinesTableComponent {
  @Input({ required: true }) lines: readonly CreatePurchaseOrderLineRequest[] = [];
  @Output() readonly removeLine = new EventEmitter<string>();
}
