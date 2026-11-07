import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslocoPipe } from '@ngneat/transloco';
import { SupplierReturnDetail } from '../services/supplier-return.models';
@Component({
  selector: 'app-supplier-return-print-view',
  standalone: true,
  imports: [CommonModule, TranslocoPipe],
  template: `
    @if (document) {
      <article class="reference">
        <h1>{{ 'supplierReturns.printTitle' | transloco }}</h1>
        <p>{{ 'supplierReturns.commercialDisclaimer' | transloco }}</p>
        <h2>{{ document.returnNumber }}</h2>
        <p>{{ document.shopName }} → {{ document.supplierName }}</p>
        <p>
          {{ 'supplierReturns.shippedAt' | transloco }}: {{ document.shippedAt | date: 'medium' }}
        </p>
        <p>{{ document.reason }}</p>
        <table>
          <thead>
            <tr>
              <th>{{ 'supplierReturns.item' | transloco }}</th>
              <th>{{ 'supplierReturns.quantity' | transloco }}</th>
              <th>{{ 'supplierReturns.source' | transloco }}</th>
              <th>{{ 'supplierReturns.acceptedCredit' | transloco }}</th>
            </tr>
          </thead>
          <tbody>
            @for (line of document.lines; track line.returnLineId) {
              <tr>
                <td>{{ line.itemName }}</td>
                <td>{{ line.quantity }} {{ line.uqc }}</td>
                <td>
                  {{ line.batchNumberSnapshot || line.receiptValueId }}
                  @if (line.sourceReference) {
                    <br />{{ line.sourceReference }}
                  }
                </td>
                <td>
                  {{
                    line.acceptedAmount === null
                      ? ('supplierReturns.pending' | transloco)
                      : (line.acceptedAmount | number: '1.2-2')
                  }}
                </td>
              </tr>
            }
          </tbody>
        </table>
        @if (document.credit) {
          <p>
            {{ 'supplierReturns.acceptedAt' | transloco }}:
            {{ document.credit.acceptedAt | date: 'medium' }}
          </p>
          <p>{{ document.credit.reason }} {{ document.credit.supplierReference }}</p>
        } @else {
          <p>{{ 'supplierReturns.pending' | transloco }}</p>
        }
      </article>
    }
  `,
  styles: [
    `
      table {
        width: 100%;
        border-collapse: collapse;
        table-layout: fixed;
      }
      td,
      th {
        overflow-wrap: anywhere;
        padding: 0.6rem;
        text-align: left;
        border-bottom: 1px solid #ddd;
      }
      h2,
      p {
        overflow-wrap: anywhere;
      }
      .reference {
        padding: 1rem;
        background: white;
        color: #23180f;
      }
      @media print {
        .reference {
          padding: 0;
        }
      }
    `,
  ],
})
export class SupplierReturnPrintViewComponent {
  @Input({ required: true }) document!: SupplierReturnDetail;
}
