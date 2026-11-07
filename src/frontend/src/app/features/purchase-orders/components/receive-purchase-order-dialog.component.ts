import { CommonModule } from '@angular/common';
import {
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  effect,
  Output,
  SimpleChanges,
  inject,
  signal,
} from '@angular/core';
import { FormArray, FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { AutoCompleteCompleteEvent } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DatePickerModule } from 'primeng/datepicker';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { firstValueFrom, Subscription } from 'rxjs';
import { ShopContextService } from '../../../core/context/shop-context.service';
import type { ShopScope } from '../../../core/context/shop-scope';
import { parseDateOnlyAsLocalDate } from '../../../shared/utils/date-time.util';
import { ReceivingDraftService } from '../services/receiving-draft.service';
import type {
  ReceivingCommand,
  ReceivingCorrelation,
  ReceivingDraftRecord,
  ReceivingEditableDraft,
  ReceivingRowError,
} from '../services/receiving.models';
import { PurchaseOrdersFacade } from '../state/purchase-orders.facade';

import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { InventoryBarcodeFieldComponent } from '../../inventory/components/barcode-field.component';
import { PurchaseOrderDetail, PurchaseOrderLine } from '../services/purchase-order.service';
import { formatLocalIsoDate } from '../../../shared/utils/date-time.util';
import { InventoryBatchDefaultsService } from '../../inventory/services/inventory-batch-defaults.service';
import { InventoryService } from '../../inventory/services/inventory.service';
import { InputValidators } from '../../../shared/forms/input-validation';

@Component({
  selector: 'app-receive-purchase-order-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    TranslocoPipe,
    ButtonModule,
    CheckboxModule,
    DatePickerModule,
    DialogModule,
    InputGroupAddonModule,
    InputGroupModule,
    InputNumberModule,
    InputTextModule,
    SelectModule,
    TextareaModule,
    InventoryBarcodeFieldComponent,
  ],
  template: `
    <p-dialog
      [header]="'purchaseOrders.receiveDialog.title' | transloco"
      [visible]="visible"
      [closable]="canClose()"
      [closeOnEscape]="false"
      [modal]="true"
      [style]="{ width: 'min(720px, 96vw)' }"
      (onHide)="hide()"
    >
      @if (status() === 'unknown') {
        <p role="status">{{ 'purchaseOrders.receiving.unknown' | transloco }}</p>
        <button
          pButton
          type="button"
          [disabled]="busy()"
          [label]="'purchaseOrders.receiving.check' | transloco"
          (click)="checkReceipt()"
        ></button>
        <button
          pButton
          type="button"
          [disabled]="busy()"
          [label]="'purchaseOrders.receiving.retry' | transloco"
          (click)="retryReceipt()"
        ></button>
      }
      @if (status() === 'confirmed') {
        <p role="status">{{ 'purchaseOrders.status.Received' | transloco }}</p>
      }
      @if (busy()) {
        <p role="status">{{ 'purchaseOrders.receiving.sending' | transloco }}</p>
      }
      @if (errorMessage()) {
        <p class="error" role="alert">{{ errorMessage() | transloco }}</p>
      }
      <form class="receive-form" [formGroup]="form" (ngSubmit)="submit()">
        <label>
          {{ 'purchaseOrders.receiveDialog.reference' | transloco }}
          <input pInputText formControlName="referenceNumber" />
        </label>
        <label class="full-width">
          {{ 'purchaseOrders.receiveDialog.notes' | transloco }}
          <textarea pTextarea formControlName="notes"></textarea>
        </label>
        <label class="full-width">
          {{ 'purchaseOrders.receiveDialog.searchLines' | transloco }}
          <input
            pInputText
            data-testid="receive-line-search"
            [value]="lineSearch()"
            (input)="lineSearch.set($any($event.target).value)"
          />
        </label>
        <div class="receipt-lines" formArrayName="lines">
          @for (index of filteredLineIndexes(); track lines.at(index).controls.clientLineId.value) {
            @let row = lines.at(index);
            <div class="receipt-row" data-testid="receipt-row" [formGroupName]="index">
              @for (error of errorsFor(row.controls.clientLineId.value); track $index) {
                <p class="error" role="alert" [attr.data-field]="error.field">
                  {{ error.messageKey | transloco }}
                </p>
              }
              <label>
                {{ 'purchaseOrders.receiveDialog.line' | transloco }}
                <p-select
                  formControlName="purchaseOrderLineId"
                  [options]="receivableLines"
                  optionLabel="description"
                  optionValue="lineId"
                  [filter]="true"
                  filterBy="description"
                  [placeholder]="'purchaseOrders.receiveDialog.selectLine' | transloco"
                  (onChange)="onLineSelectionChange(index)"
                />
              </label>
              <app-inventory-barcode-field
                [control]="row.controls.barcode"
                [suggestions]="barcodeSuggestionsFor(index)"
                [barcodeGenerating]="isBarcodeGenerating(index)"
                [barcodeGenerateError]="barcodeGenerateErrorFor(index)"
                [barcodeReplaceConfirmVisible]="isBarcodeReplaceConfirmVisible(index)"
                [showScanner]="false"
                (filterRequested)="onBarcodeFilter(index, $event)"
                (selected)="onBarcodeSelected(index, $event)"
                (generateRequested)="generateBarcode(index)"
                (confirmReplace)="confirmBarcodeReplace(index)"
                (cancelReplace)="cancelBarcodeReplace(index)"
              />
              <label>
                {{ 'purchaseOrders.receiveDialog.batchNumber' | transloco }}
                <p-inputgroup>
                  <input pInputText formControlName="batchNumber" />
                  <p-inputgroup-addon>
                    <button
                      pButton
                      type="button"
                      icon="pi pi-refresh"
                      severity="secondary"
                      text
                      [disabled]="frozen()"
                      (click)="generateBatchNumber(index)"
                      [attr.aria-label]="
                        'purchaseOrders.receiveDialog.generateBatchNumber' | transloco
                      "
                    ></button>
                  </p-inputgroup-addon>
                </p-inputgroup>
              </label>
              <label>
                {{ 'purchaseOrders.receiveDialog.quantity' | transloco }}
                <span
                  >{{ 'purchaseOrders.receiving.remaining' | transloco }}:
                  {{ remainingFor(row.controls.purchaseOrderLineId.value) }}</span
                >
                <p-inputNumber formControlName="quantity" [min]="1" />
              </label>
              <label>
                {{ 'purchaseOrders.receiveDialog.totalPurchaseCost' | transloco }}
                <p-inputNumber
                  formControlName="totalPurchaseCost"
                  mode="decimal"
                  [min]="0"
                  [minFractionDigits]="2"
                />
              </label>
              <label>
                {{ 'purchaseOrders.receiveDialog.mrp' | transloco }}
                <p-inputNumber
                  formControlName="mrp"
                  mode="decimal"
                  [min]="0"
                  [minFractionDigits]="2"
                />
              </label>
              <label>
                {{ 'purchaseOrders.receiveDialog.salesPrice' | transloco }}
                <p-inputNumber
                  formControlName="salesPrice"
                  mode="decimal"
                  [min]="0"
                  [minFractionDigits]="2"
                />
              </label>
              <label>
                {{ 'purchaseOrders.receiveDialog.taxRate' | transloco }}
                <p-inputgroup>
                  <p-inputNumber
                    formControlName="taxRatePercent"
                    mode="decimal"
                    [min]="0"
                    [max]="100"
                  />
                  <p-inputgroup-addon>
                    <button
                      pButton
                      type="button"
                      icon="pi pi-search"
                      severity="secondary"
                      text
                      [loading]="isTaxLookupLoading(index)"
                      [disabled]="frozen()"
                      (click)="lookupTaxForLine(index)"
                      [attr.aria-label]="'purchaseOrders.receiveDialog.lookupTaxRate' | transloco"
                    ></button>
                  </p-inputgroup-addon>
                </p-inputgroup>
              </label>
              <label>
                {{ 'purchaseOrders.receiveDialog.expiryDate' | transloco }}
                <p-datepicker
                  ngSkipHydration
                  formControlName="expiryDate"
                  dateFormat="dd/mm/yy"
                  [showIcon]="true"
                  [showButtonBar]="true"
                  appendTo="body"
                />
              </label>
              <label>
                {{ 'purchaseOrders.receiveDialog.manufacturingDate' | transloco }}
                <p-datepicker
                  ngSkipHydration
                  formControlName="manufacturingDate"
                  dateFormat="dd/mm/yy"
                  [showIcon]="true"
                  [showButtonBar]="true"
                  appendTo="body"
                />
              </label>
              <label class="checkbox-row">
                <p-checkbox formControlName="taxIncluded" [binary]="true" />
                {{ 'purchaseOrders.receiveDialog.taxIncluded' | transloco }}
              </label>
              <label class="checkbox-row">
                <p-checkbox formControlName="purchaseTaxIncluded" [binary]="true" />
                {{ 'purchaseOrders.receiveDialog.purchaseTaxIncluded' | transloco }}
              </label>
              <button
                pButton
                type="button"
                severity="secondary"
                [label]="'purchaseOrders.receiveDialog.removeLine' | transloco"
                (click)="removeLine(index)"
                [disabled]="lines.length === 1 || frozen()"
              ></button>
            </div>
          }
        </div>
        <button
          pButton
          type="button"
          severity="secondary"
          [label]="'purchaseOrders.receiving.addLot' | transloco"
          (click)="addLine()"
          [disabled]="frozen()"
        ></button>
        @if (remainingError()) {
          <p class="error" role="alert">
            {{ 'purchaseOrders.receiveDialog.quantityOverRemaining' | transloco }}
          </p>
        }
        @if (duplicateLotError()) {
          <p class="error" role="alert">
            {{ 'purchaseOrders.receiving.duplicateLot' | transloco }}
          </p>
        }
        @if ((!lines.length || !canReceiveNew()) && !busy() && status() === 'editing') {
          <p role="status">{{ 'purchaseOrders.receiving.noReceivable' | transloco }}</p>
        }
        <div class="actions">
          <button
            pButton
            type="button"
            severity="secondary"
            [label]="'purchaseOrders.actions.cancel' | transloco"
            (click)="hide()"
            [disabled]="!canClose()"
          ></button>
          <button
            pButton
            type="submit"
            [label]="'purchaseOrders.actions.receive' | transloco"
            [disabled]="
              form.invalid ||
              remainingError() ||
              duplicateLotError() ||
              frozen() ||
              lines.length === 0 ||
              submitting
            "
          ></button>
        </div>
      </form>
    </p-dialog>
  `,
  styles: [
    `
      .receive-form {
        display: grid;
        grid-template-columns: repeat(2, minmax(0, 1fr));
        gap: 0.75rem;
      }
      label {
        display: grid;
        gap: 0.25rem;
        font-size: 0.875rem;
      }
      .full-width,
      .actions,
      .error,
      .receipt-lines {
        grid-column: 1 / -1;
      }
      .receipt-lines {
        display: grid;
        gap: 0.75rem;
      }
      .receipt-row {
        display: grid;
        grid-template-columns: repeat(3, minmax(0, 1fr));
        gap: 0.75rem;
        padding: 0.75rem;
        border: 1px solid var(--p-content-border-color);
        border-radius: 6px;
      }
      .checkbox-row {
        display: flex;
        align-items: center;
        gap: 0.5rem;
      }
      .actions {
        display: flex;
        justify-content: flex-end;
        gap: 0.5rem;
        margin-top: 0.5rem;
      }
      .error {
        color: var(--p-red-600);
        margin: 0;
      }
      @media (max-width: 640px) {
        .receive-form {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
})
export class ReceivePurchaseOrderDialogComponent implements OnChanges, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly batchDefaults = inject(InventoryBatchDefaultsService);
  private readonly inventoryService = inject(InventoryService);
  private readonly catalogSync = inject(ProductCatalogSyncService);
  private readonly drafts = inject(ReceivingDraftService);
  private readonly context = inject(ShopContextService);
  private readonly facade = inject(PurchaseOrdersFacade);
  protected readonly status = signal<'editing' | 'sending' | 'unknown' | 'confirmed'>('editing');
  protected readonly errorMessage = signal('');
  protected readonly rowErrors = signal<readonly ReceivingRowError[]>([]);
  protected readonly loading = signal(false);
  private owner: ShopScope | null = null;
  private record: ReceivingDraftRecord | null = null;
  private readonly correlationSignal = signal<ReceivingCorrelation | null>(null);
  private get correlation() {
    return this.correlationSignal();
  }
  private set correlation(value: ReceivingCorrelation | null) {
    this.correlationSignal.set(value);
  }
  private initializedPo = '';
  private saveQueue: Promise<void> = Promise.resolve();
  private saveFailed = false;
  private valueChanges?: Subscription;
  private unregisterLeave?: () => void;
  private unregisterDetach?: () => void;
  private generation = 0;
  private closing = false;
  private readonly revisions = new Map<string, number>();
  private handlingResult = '';

  constructor() {
    effect(() => {
      const correlation = this.correlation;
      if (!correlation) return;
      const result = this.facade.receivingState(correlation);
      if (!result || !this.current() || this.handlingResult === JSON.stringify(result)) return;
      this.handlingResult = JSON.stringify(result);
      if (result.status === 'confirmed') {
        this.status.set('confirmed');
        void this.closeConfirmed();
      } else if (result.status === 'rejected' || result.status === 'unknown') {
        this.errorMessage.set(result.errorMessage);
        this.rowErrors.set(result.rowErrors);
        this.status.set(result.status === 'rejected' ? 'editing' : 'unknown');
        void this.reloadSettled(result.status === 'rejected');
      } else this.loading.set(true);
    });
  }

  @Input({ required: true }) order!: PurchaseOrderDetail;
  @Input() visible = false;
  @Input() submitting = false;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() closed = new EventEmitter<void>();
  @Output() receive = new EventEmitter<ReceivingCommand>();

  protected receivableLines: PurchaseOrderLine[] = [];
  protected readonly taxLookupLoading = signal<Record<string, boolean>>({});
  protected readonly lineSearch = signal('');
  protected readonly barcodeSuggestions = signal<Record<string, readonly string[]>>({});
  protected readonly barcodeGenerating = signal<Record<string, boolean>>({});
  protected readonly barcodeGenerateErrors = signal<Record<string, string>>({});
  protected readonly barcodeReplaceConfirmVisible = signal<Record<string, boolean>>({});
  private readonly pendingGeneratedBarcode = new Map<string, string>();

  protected readonly form = this.fb.nonNullable.group({
    referenceNumber: [''],
    notes: [''],
    lines: this.fb.array([this.createLineGroup()]),
  });

  protected get lines(): FormArray<
    ReturnType<ReceivePurchaseOrderDialogComponent['createLineGroup']>
  > {
    return this.form.controls.lines;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.order) return;
    this.receivableLines = [...this.order.lines];
    if (!this.canReceiveNew()) this.form.disable({ emitEvent: false });
    if (this.visible && this.initializedPo !== this.order.purchaseOrderId) void this.initialize();
  }

  ngOnDestroy(): void {
    void this.flush();
    this.generation++;
    this.valueChanges?.unsubscribe();
    this.unregisterLeave?.();
    this.unregisterDetach?.();
  }

  protected canClose(): boolean {
    return !this.closing && (!this.loading() || !!this.record?.pending);
  }

  protected busy(): boolean {
    return this.closing || this.loading() || this.status() === 'sending';
  }
  protected frozen(): boolean {
    return this.closing || this.loading() || this.status() !== 'editing' || !this.canReceiveNew();
  }
  protected canReceiveNew(): boolean {
    return this.order.status === 'Placed' || this.order.status === 'PartiallyReceived';
  }
  private current(): boolean {
    return (
      !!this.owner &&
      this.context.isCurrent(this.owner) &&
      this.context.epoch() === this.owner.epoch
    );
  }
  protected errorsFor(id: string): readonly ReceivingRowError[] {
    return this.rowErrors().filter((error) => error.clientLineId === id);
  }

  private async initialize(): Promise<void> {
    this.valueChanges?.unsubscribe();
    this.unregisterLeave?.();
    this.unregisterDetach?.();
    const generation = ++this.generation;
    this.initializedPo = this.order.purchaseOrderId;
    this.owner = this.context.scope();
    this.record = null;
    this.correlation = null;
    this.loading.set(true);
    this.status.set('editing');
    this.saveFailed = false;
    this.errorMessage.set('');
    this.rowErrors.set([]);
    this.lines.clear({ emitEvent: false });
    this.order.lines
      .filter((line) => this.remainingFor(line.lineId) > 0)
      .forEach((line) => this.lines.push(this.createLineGroup(line), { emitEvent: false }));
    this.form.disable({ emitEvent: false });
    if (!this.owner) {
      this.errorMessage.set('purchaseOrders.receiving.scopeError');
      this.loading.set(false);
      return;
    }
    const owner = this.owner;
    const poId = this.initializedPo;
    this.unregisterLeave = this.context.onLeave(async () => {
      if (this.busy()) return false;
      return this.flush();
    });
    this.unregisterDetach = this.context.onDetach(() => {
      const saved = this.flush();
      this.generation++;
      this.visible = false;
      this.form.disable({ emitEvent: false });
      this.valueChanges?.unsubscribe();
      this.owner = null;
      this.lines.clear({ emitEvent: false });
      this.form.reset({ referenceNumber: '', notes: '' }, { emitEvent: false });
      return saved;
    });
    try {
      const stored = await this.drafts.load(owner, poId);
      if (generation !== this.generation || !this.current()) return;
      this.record = stored;
      this.revisions.set(JSON.stringify([owner.userId, owner.shopId, poId]), stored?.revision ?? 0);
      if (stored) this.restore(stored.draft);
      if (stored?.confirmedOperationId) {
        this.status.set('confirmed');
        this.form.disable({ emitEvent: false });
      } else if (stored?.pending) {
        this.status.set('unknown');
        this.form.disable({ emitEvent: false });
        this.correlation = {
          scope: owner,
          purchaseOrderId: poId,
          operationId: stored.pending.operationId,
        };
      } else {
        if (this.canReceiveNew()) this.form.enable({ emitEvent: false });
        this.valueChanges = this.form.valueChanges.subscribe(() => this.queueSave());
        if (!stored)
          this.lines.controls.forEach((_, index) => {
            void this.applyTaxDefault(index);
          });
      }
    } catch {
      this.saveFailed = true;
      this.errorMessage.set('purchaseOrders.receiving.storageError');
    } finally {
      if (generation === this.generation) this.loading.set(false);
    }
  }

  private restore(draft: ReceivingEditableDraft): void {
    this.form.patchValue(
      { referenceNumber: draft.referenceNumber, notes: draft.notes },
      { emitEvent: false },
    );
    this.lines.clear({ emitEvent: false });
    for (const line of draft.lines) {
      const row = this.createLineGroup();
      row.patchValue(
        {
          ...line,
          quantity: line.quantity as number,
          totalPurchaseCost: line.totalPurchaseCost as number,
          mrp: line.mrp as number,
          salesPrice: line.salesPrice as number,
          taxRatePercent: line.taxRatePercent as number,
          expiryDate: line.expiryDate ? parseDateOnlyAsLocalDate(line.expiryDate) : null,
          manufacturingDate: line.manufacturingDate
            ? parseDateOnlyAsLocalDate(line.manufacturingDate)
            : null,
        },
        { emitEvent: false },
      );
      this.lines.push(row, { emitEvent: false });
    }
  }
  private editable(): ReceivingEditableDraft {
    const value = this.form.getRawValue();
    return {
      referenceNumber: value.referenceNumber,
      notes: value.notes,
      receivedAt: this.record?.draft.receivedAt ?? null,
      lines: value.lines.map((line) => ({
        ...line,
        expiryDate: line.expiryDate ? formatLocalIsoDate(line.expiryDate) : null,
        manufacturingDate: line.manufacturingDate
          ? formatLocalIsoDate(line.manufacturingDate)
          : null,
      })),
    };
  }
  private queueSave(): void {
    if (!this.owner || this.status() !== 'editing' || this.loading()) return;
    const draft = this.editable();
    const owner = this.owner;
    const poId = this.initializedPo;
    const generation = this.generation;
    const key = JSON.stringify([owner.userId, owner.shopId, poId]);
    this.saveQueue = this.saveQueue.then(async () => {
      try {
        const saved = await this.drafts.save(owner, poId, draft, this.revisions.get(key) ?? 0);
        this.revisions.set(key, saved.revision);
        if (generation === this.generation) {
          this.record = saved;
          this.saveFailed = false;
        }
      } catch {
        if (generation === this.generation) {
          this.saveFailed = true;
          this.errorMessage.set('purchaseOrders.receiving.storageError');
        }
      }
    });
  }
  private async flush(): Promise<boolean> {
    if (this.status() === 'editing' && !this.loading()) this.queueSave();
    await this.saveQueue;
    return !this.saveFailed;
  }
  private async reloadSettled(editable: boolean): Promise<void> {
    const generation = this.generation;
    this.loading.set(true);
    try {
      const record = await this.drafts.load(this.owner!, this.initializedPo);
      if (generation !== this.generation || !this.current()) return;
      this.record = record;
      this.revisions.set(
        JSON.stringify([this.owner!.userId, this.owner!.shopId, this.initializedPo]),
        record?.revision ?? 0,
      );
      if (record?.confirmedOperationId === this.correlation?.operationId) {
        this.status.set('confirmed');
        await this.closeConfirmed();
        return;
      }
      if (editable && !record?.pending && !record?.confirmedOperationId) {
        this.form.enable({ emitEvent: false });
        this.valueChanges?.unsubscribe();
        this.valueChanges = this.form.valueChanges.subscribe(() => this.queueSave());
      } else this.form.disable({ emitEvent: false });
    } catch {
      this.status.set('unknown');
      this.errorMessage.set('purchaseOrders.receiving.storageError');
    } finally {
      if (generation === this.generation) this.loading.set(false);
    }
  }
  private async closeConfirmed(): Promise<void> {
    this.loading.set(false);
    // A failed delete leaves a noneditable tombstone, never the old editable draft.
    try {
      const record = await this.drafts.load(this.owner!, this.initializedPo);
      if (record?.confirmedOperationId)
        await this.drafts.discard(this.owner!, this.initializedPo, record.revision);
    } catch {
      /* Safe to close: confirmation was already committed to IndexedDB. */
    }
    if (this.current()) this.close();
  }

  protected filteredLineIndexes(): number[] {
    const searchTerm = this.lineSearch().trim().toLocaleLowerCase();
    return this.lines.controls
      .map((row, index) => ({ index, line: this.findLine(row.controls.purchaseOrderLineId.value) }))
      .filter(
        ({ line }) => !searchTerm || line?.description.toLocaleLowerCase().includes(searchTerm),
      )
      .map(({ index }) => index);
  }

  protected remainingFor(lineId: string): number {
    const line = this.receivableLines.find((item) => item.lineId === lineId);
    return line?.remainingQuantity ?? line?.expectedQuantity ?? 0;
  }

  protected remainingError(): boolean {
    const totals = new Map<string, number>();
    for (const row of this.lines.controls) {
      const id = row.controls.purchaseOrderLineId.value;
      const quantity = row.controls.quantity.value;
      if (!Number.isInteger(quantity) || quantity <= 0) return true;
      totals.set(id, (totals.get(id) ?? 0) + quantity);
    }
    return [...totals].some(([id, total]) => total > this.remainingFor(id));
  }
  protected duplicateLotError(): boolean {
    const identities = this.lines.controls.map((row) =>
      JSON.stringify([
        this.findLine(row.controls.purchaseOrderLineId.value)?.itemId,
        row.controls.batchNumber.value.trim(),
      ]),
    );
    return new Set(identities).size !== identities.length;
  }
  protected addLine(): void {
    if (this.frozen()) return;
    const line =
      this.findLine(this.lines.at(this.lines.length - 1)?.controls.purchaseOrderLineId.value) ??
      this.receivableLines.find((line) => this.remainingFor(line.lineId) > 0);
    if (!line) return;
    const row = this.createLineGroup(line);
    row.controls.quantity.setValue(1, { emitEvent: false });
    row.controls.totalPurchaseCost.setValue(line.unitCost, { emitEvent: false });
    this.lines.push(row);
    void this.applyTaxDefault(this.lines.length - 1);
  }

  protected removeLine(index: number): void {
    if (!this.frozen() && this.lines.length > 1) this.lines.removeAt(index);
  }

  protected onLineSelectionChange(index: number): void {
    if (this.frozen()) return;
    const row = this.lines.at(index);
    const line = this.findLine(row.controls.purchaseOrderLineId.value);
    if (!line) {
      return;
    }

    const remaining = line.remainingQuantity ?? line.expectedQuantity;
    if (!row.controls.quantity.dirty) {
      row.controls.quantity.setValue(remaining);
    }
    if (!row.controls.totalPurchaseCost.dirty) {
      row.controls.totalPurchaseCost.setValue((line.unitCost ?? 0) * remaining);
    }
    if (!row.controls.batchNumber.value.trim()) {
      row.controls.batchNumber.setValue(this.batchDefaults.generateBatchNumber());
    }
    if (!row.controls.barcode.dirty) {
      row.controls.barcode.setValue(this.barcodeForLine(line));
    }
    void this.applyTaxDefault(index);
  }

  protected onBarcodeFilter(index: number, event: AutoCompleteCompleteEvent): void {
    this.setRecord(
      this.barcodeSuggestions,
      index,
      this.catalogSync.filterByBarcode(event.query).map((entry) => entry.barcode),
    );
  }

  protected onBarcodeSelected(index: number, barcode: string): void {
    if (this.frozen()) return;
    const row = this.lines.at(index);
    row.controls.barcode.setValue(barcode);
    row.controls.barcode.markAsDirty();
  }

  protected barcodeSuggestionsFor(index: number): string[] {
    return [...(this.barcodeSuggestions()[this.rowKey(index)] ?? [])];
  }

  protected isBarcodeGenerating(index: number): boolean {
    return this.barcodeGenerating()[this.rowKey(index)] ?? false;
  }

  protected barcodeGenerateErrorFor(index: number): string {
    return this.barcodeGenerateErrors()[this.rowKey(index)] ?? '';
  }

  protected isBarcodeReplaceConfirmVisible(index: number): boolean {
    return this.barcodeReplaceConfirmVisible()[this.rowKey(index)] ?? false;
  }

  protected async generateBarcode(index: number): Promise<void> {
    const row = this.lines.at(index);
    if (this.frozen() || !row || this.barcodeGenerating()[this.rowKey(index)]) {
      return;
    }

    const original = row.getRawValue();
    const generation = this.generation;
    this.setRecord(this.barcodeGenerating, index, true);
    this.setRecord(this.barcodeGenerateErrors, index, '');
    try {
      const barcode = (
        await firstValueFrom(this.inventoryService.generateItemBarcode())
      ).barcode.trim();
      if (
        this.frozen() ||
        generation !== this.generation ||
        !this.lines.controls.includes(row) ||
        JSON.stringify(row.getRawValue()) !== JSON.stringify(original)
      )
        return;
      index = this.lines.controls.indexOf(row);
      if (!barcode) {
        this.setRecord(this.barcodeGenerateErrors, index, 'inventory.generateBarcodeError');
        return;
      }

      if (row.controls.barcode.value.trim()) {
        this.pendingGeneratedBarcode.set(this.rowKey(index), barcode);
        this.setRecord(this.barcodeReplaceConfirmVisible, index, true);
        return;
      }

      row.controls.barcode.setValue(barcode);
      row.controls.barcode.markAsDirty();
    } catch {
      const live = this.lines.controls.indexOf(row);
      if (live >= 0 && generation === this.generation && !this.frozen())
        this.setRecord(this.barcodeGenerateErrors, live, 'inventory.generateBarcodeError');
    } finally {
      const live = this.lines.controls.indexOf(row);
      if (live >= 0) this.setRecord(this.barcodeGenerating, live, false);
    }
  }

  protected confirmBarcodeReplace(index: number): void {
    if (this.frozen()) return;
    const barcode = this.pendingGeneratedBarcode.get(this.rowKey(index));
    const row = this.lines.at(index);
    if (barcode && row) {
      row.controls.barcode.setValue(barcode);
      row.controls.barcode.markAsDirty();
      this.setRecord(this.barcodeGenerateErrors, index, '');
    }
    this.pendingGeneratedBarcode.delete(this.rowKey(index));
    this.setRecord(this.barcodeReplaceConfirmVisible, index, false);
  }

  protected cancelBarcodeReplace(index: number): void {
    this.pendingGeneratedBarcode.delete(this.rowKey(index));
    this.setRecord(this.barcodeReplaceConfirmVisible, index, false);
  }

  protected generateBatchNumber(index: number): void {
    if (this.frozen()) return;
    const row = this.lines.at(index);
    row.controls.batchNumber.setValue(this.batchDefaults.generateBatchNumber());
    row.controls.batchNumber.markAsDirty();
  }

  protected lookupTaxForLine(index: number): void {
    void this.applyTaxDefault(index, { respectDirty: false });
  }

  protected isTaxLookupLoading(index: number): boolean {
    return this.taxLookupLoading()[this.rowKey(index)] ?? false;
  }

  private createLineGroup(line?: PurchaseOrderLine) {
    const remaining = line ? (line.remainingQuantity ?? line.expectedQuantity) : 1;
    return this.fb.nonNullable.group(
      {
        clientLineId: [crypto.randomUUID() as string],
        purchaseOrderLineId: [line?.lineId ?? '', InputValidators.requiredText()],
        barcode: [line ? this.barcodeForLine(line) : '', InputValidators.requiredText(120)],
        batchNumber: [this.batchDefaults.generateBatchNumber(), InputValidators.requiredText(80)],
        quantity: [remaining, InputValidators.positiveNumber()],
        totalPurchaseCost: [(line?.unitCost ?? 0) * remaining, InputValidators.nonNegativeNumber()],
        mrp: [0, InputValidators.nonNegativeNumber()],
        salesPrice: [0, InputValidators.nonNegativeNumber()],
        taxRatePercent: [0, InputValidators.percentage()],
        taxIncluded: [false],
        purchaseTaxIncluded: [false],
        expiryDate: [null as Date | null],
        manufacturingDate: [null as Date | null],
      },
      {
        validators: InputValidators.fieldLessThanOrEqual(
          'salesPrice',
          'mrp',
          'salesPriceExceedsMrp',
        ),
      },
    );
  }

  private async applyTaxDefault(
    index: number,
    options: { readonly respectDirty?: boolean } = {},
  ): Promise<void> {
    const respectDirty = options.respectDirty ?? true;
    const row = this.lines.at(index);
    if (
      !row ||
      this.status() !== 'editing' ||
      (respectDirty && row.controls.taxRatePercent.dirty)
    ) {
      return;
    }

    const line = this.findLine(row.controls.purchaseOrderLineId.value);
    const productName = line?.description.trim() ?? '';
    if (productName.length < 3) {
      return;
    }

    const original = row.getRawValue();
    const generation = this.generation;
    this.setTaxLookupLoading(index, true);
    try {
      const result = await this.batchDefaults.lookupHsn(productName);
      if (
        this.status() !== 'editing' ||
        this.loading() ||
        generation !== this.generation ||
        !this.lines.controls.includes(row) ||
        JSON.stringify(row.getRawValue()) !== JSON.stringify(original) ||
        (respectDirty && row.controls.taxRatePercent.dirty)
      )
        return;
      index = this.lines.controls.indexOf(row);
      const taxRatePercent = this.batchDefaults.getAutoTaxRatePercent(result);
      if (taxRatePercent !== null) {
        row.controls.taxRatePercent.setValue(taxRatePercent);
        if (!respectDirty) {
          row.controls.taxRatePercent.markAsDirty();
        }
      }
    } catch {
      // Keep manual tax entry available when lookup fails.
    } finally {
      const live = this.lines.controls.indexOf(row);
      if (live >= 0) this.setTaxLookupLoading(live, false);
    }
  }

  private setTaxLookupLoading(index: number, loading: boolean): void {
    this.setRecord(this.taxLookupLoading, index, loading || undefined);
  }

  private findLine(lineId: string): PurchaseOrderLine | undefined {
    return this.receivableLines.find((line) => line.lineId === lineId);
  }

  private barcodeForLine(line: PurchaseOrderLine): string {
    return (
      this.catalogSync.catalogEntries().find((entry) => entry.itemId === line.itemId)?.barcode ?? ''
    );
  }

  private rowKey(index: number): string {
    return this.lines.at(index)?.controls.clientLineId.value ?? '';
  }

  private setRecord<T>(
    target: { (): Record<string, T>; set(value: Record<string, T>): void },
    index: number,
    value: T | undefined,
  ): void {
    const key = this.rowKey(index);
    const current = { ...target() };
    if (value === undefined || value === '' || value === false) {
      delete current[key];
    } else {
      current[key] = value;
    }
    target.set(current);
  }

  protected async hide(): Promise<void> {
    if (!this.canClose()) return;
    const saving = this.flush();
    this.closing = true;
    const saved = await saving;
    this.closing = false;
    if (!saved || !this.canClose()) return;
    if (this.status() === 'confirmed') {
      await this.closeConfirmed();
      return;
    }
    this.close();
  }
  private close(): void {
    this.visible = false;
    this.visibleChange.emit(false);
    this.closed.emit();
    this.initializedPo = '';
    this.valueChanges?.unsubscribe();
    this.unregisterLeave?.();
    this.unregisterDetach?.();
  }
  protected async submit(): Promise<void> {
    if (
      this.frozen() ||
      this.form.invalid ||
      this.remainingError() ||
      this.duplicateLotError() ||
      !this.lines.length ||
      !this.owner ||
      !this.current()
    )
      return;
    this.loading.set(true);
    this.form.disable({ emitEvent: false });
    const generation = this.generation;
    await this.saveQueue;
    if (this.saveFailed) {
      this.loading.set(false);
      this.form.enable({ emitEvent: false });
      return;
    }
    try {
      const record = await this.drafts.prepare(
        this.owner,
        this.initializedPo,
        this.editable(),
        this.record?.revision ?? 0,
      );
      if (generation !== this.generation || !this.current()) return;
      this.record = record;
      this.restore(record.draft);
      this.form.disable({ emitEvent: false });
      if (!record.pending) return;
      this.correlation = {
        scope: this.owner,
        purchaseOrderId: this.initializedPo,
        operationId: record.pending.operationId,
      };
      this.handlingResult = '';
      this.status.set('sending');
      this.rowErrors.set([]);
      this.errorMessage.set('');
      this.receive.emit({
        correlation: this.correlation,
        payload: record.pending.payload,
        recovery: record.pending.status === 'unknown',
      });
    } catch {
      this.errorMessage.set('purchaseOrders.receiving.storageError');
      this.form.enable({ emitEvent: false });
    } finally {
      if (generation === this.generation) this.loading.set(false);
    }
  }
  protected retryReceipt(): void {
    if (this.busy() || !this.record?.pending || !this.correlation || !this.current()) return;
    this.status.set('sending');
    this.handlingResult = '';
    this.receive.emit({
      correlation: this.correlation,
      payload: this.record.pending.payload,
      recovery: true,
    });
  }
  protected checkReceipt(): void {
    if (this.busy() || !this.correlation || !this.current()) return;
    this.loading.set(true);
    this.handlingResult = '';
    this.facade.checkReceipt(this.correlation);
  }
}
