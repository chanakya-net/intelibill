import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { AutoCompleteCompleteEvent, AutoCompleteModule } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

@Component({
  selector: 'app-inventory-barcode-field',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    AutoCompleteModule,
    ButtonModule,
    InputGroupModule,
    InputGroupAddonModule,
    ProgressSpinnerModule,
  ],
  template: `
    <label>
      <span>{{ 'inventory.barcode' | transloco }}</span>
      <div class="barcode-field-wrapper" [style.position]="'relative'">
        <p-inputgroup class="barcode-input-group">
          <p-autocomplete
            class="barcode-autocomplete"
            [formControl]="control"
            [suggestions]="suggestions"
            (completeMethod)="filterRequested.emit($event)"
            (onSelect)="selected.emit($event.value)"
            (focusout)="focusLost.emit()"
            appendTo="body"
            [fluid]="true"
          ></p-autocomplete>
          @if (showScanner) {
            <p-inputgroup-addon class="camera-addon">
              <button
                pButton
                type="button"
                icon="pi pi-camera"
                severity="secondary"
                text
                class="camera-button"
                (click)="scannerRequested.emit()"
                [attr.aria-label]="'inventory.openScanner' | transloco"
              ></button>
            </p-inputgroup-addon>
          }
          <p-inputgroup-addon class="generate-barcode-addon">
            <button
              pButton
              type="button"
              [label]="'inventory.generateBarcode' | transloco"
              severity="secondary"
              outlined
              class="generate-barcode-button"
              [loading]="barcodeGenerating"
              (click)="generateRequested.emit()"
              [attr.aria-label]="'inventory.generateBarcode' | transloco"
            ></button>
          </p-inputgroup-addon>
        </p-inputgroup>
        @if (loadingProduct) {
          <div class="barcode-loading-overlay">
            <p-progressSpinner [style]="{ width: '1.5rem', height: '1.5rem' }" strokeWidth="6"></p-progressSpinner>
          </div>
        }
      </div>
      @if (barcodeGenerateError) {
        <p class="error-message">{{ barcodeGenerateError | transloco }}</p>
      }
      @if (barcodeReplaceConfirmVisible) {
        <div class="barcode-replace-confirm">
          <span>{{ replaceConfirmKey | transloco }}</span>
          <button pButton type="button" [label]="'inventory.confirmReplace' | transloco" severity="warn" (click)="confirmReplace.emit()"></button>
          <button pButton type="button" [label]="'inventory.keepCurrentBarcode' | transloco" severity="secondary" (click)="cancelReplace.emit()"></button>
        </div>
      }
    </label>
  `,
  styles: [`
    :host { display: grid; min-width: 0; }
    label { display: grid; gap: .25rem; font-size: .875rem; min-width: 0; }
    .barcode-field-wrapper { position: relative; min-width: 0; }
    .barcode-input-group,
    :host ::ng-deep .barcode-input-group.p-inputgroup {
      display: flex;
      align-items: stretch;
      width: 100%;
      min-width: 0;
    }
    .barcode-loading-overlay {
      position: absolute;
      top: 0;
      right: 8px;
      bottom: 0;
      display: flex;
      align-items: center;
      justify-content: center;
      pointer-events: none;
      opacity: .7;
    }
    .error-message { margin: 0; color: var(--p-red-600); font-size: .875rem; }
    .barcode-replace-confirm { display: flex; align-items: center; flex-wrap: wrap; gap: .5rem; }
    .barcode-replace-confirm span { flex: 1 1 10rem; }
    :host ::ng-deep .barcode-input-group .p-autocomplete {
      flex: 1 1 auto;
      min-width: 0;
      width: 1%;
    }
    :host ::ng-deep .barcode-input-group .p-autocomplete-input,
    :host ::ng-deep .barcode-input-group .p-inputtext {
      width: 100%;
    }
    :host ::ng-deep .barcode-input-group .p-inputgroupaddon {
      flex: 0 0 auto;
      display: flex;
      align-items: stretch;
      padding: 0;
    }
    :host ::ng-deep .barcode-input-group .camera-button,
    :host ::ng-deep .barcode-input-group .generate-barcode-button {
      border-radius: 0;
      height: 100%;
      min-height: 2.75rem;
    }
    :host ::ng-deep .barcode-input-group .camera-button {
      min-width: 2.75rem;
      width: 2.75rem;
      padding: 0;
    }
    :host ::ng-deep .barcode-input-group .generate-barcode-button {
      padding-inline: 0.85rem;
      white-space: nowrap;
      font-weight: 700;
    }
  `],
})
export class InventoryBarcodeFieldComponent {
  @Input({ required: true }) control!: FormControl<string>;
  @Input() suggestions: string[] = [];
  @Input() loadingProduct = false;
  @Input() barcodeGenerating = false;
  @Input() barcodeGenerateError = '';
  @Input() barcodeReplaceConfirmVisible = false;
  @Input() showScanner = true;
  @Input() replaceConfirmKey = 'inventory.generateBarcodeReplaceConfirm';

  @Output() readonly filterRequested = new EventEmitter<AutoCompleteCompleteEvent>();
  @Output() readonly selected = new EventEmitter<string>();
  @Output() readonly focusLost = new EventEmitter<void>();
  @Output() readonly scannerRequested = new EventEmitter<void>();
  @Output() readonly generateRequested = new EventEmitter<void>();
  @Output() readonly confirmReplace = new EventEmitter<void>();
  @Output() readonly cancelReplace = new EventEmitter<void>();
}
