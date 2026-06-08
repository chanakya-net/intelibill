import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnDestroy, OnInit, Output, ViewEncapsulation, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { AutoCompleteCompleteEvent, AutoCompleteModule } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';

import { AuthService } from '../../../core/auth/auth.service';
import { ShopPermissionsService } from '../../../core/layout/shop-permissions.service';
import { formatLocalIsoDate, parseDateOnlyAsLocalDate } from '../../../shared/utils/date-time.util';
import { SuppliersFacade } from '../../suppliers/state/suppliers.facade';
import { PurchaseOrderLineFormComponent } from '../components/purchase-order-line-form.component';
import { PurchaseOrderLinesTableComponent } from '../components/purchase-order-lines-table.component';
import { CreatePurchaseOrderLineRequest } from '../services/purchase-order.service';
import { PurchaseOrderDraftStateService } from '../services/purchase-order-draft-state.service';
import { PurchaseOrdersFacade } from '../state/purchase-orders.facade';

@Component({
  selector: 'app-purchase-order-builder-page',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    TranslocoPipe,
    AutoCompleteModule,
    ButtonModule,
    DatePickerModule,
    InputTextModule,
    TextareaModule,
    PurchaseOrderLineFormComponent,
    PurchaseOrderLinesTableComponent,
  ],
  template: `
    <section class="overlay" aria-modal="true" role="dialog">
      <div class="overlay-backdrop" (click)="closeRequested.emit()"></div>

      <div class="overlay-card">
        <header class="po-builder__header">
          <div>
            <p class="eyebrow">{{ 'purchaseOrders.title' | transloco }}</p>
            <h2>{{ (purchaseOrderId ? 'purchaseOrders.builder.editTitle' : 'purchaseOrders.builder.newTitle') | transloco }}</h2>
            @if (draftState.loadingDraft()) {
              <p class="autosave-hint">{{ 'purchaseOrders.builder.autosaveLoaded' | transloco }}</p>
            }
          </div>
          <button type="button" class="close-button" [attr.aria-label]="'common.close' | transloco" (click)="closeRequested.emit()">
            <i class="pi pi-times" aria-hidden="true"></i>
          </button>
        </header>

        <form class="po-builder__form" [formGroup]="form">
          <label>
            <span>{{ 'purchaseOrders.builder.supplier' | transloco }}</span>
            <p-autocomplete
              formControlName="supplierName"
              [suggestions]="supplierNameSuggestions()"
              (completeMethod)="onSupplierSearch($event)"
              appendTo="body"
              [fluid]="true"
            ></p-autocomplete>
          </label>
          <label>
            <span>{{ 'purchaseOrders.builder.orderDate' | transloco }}</span>
            <p-datepicker
              ngSkipHydration
              formControlName="orderDate"
              dateFormat="dd/mm/yy"
              [showIcon]="true"
              [showButtonBar]="true"
              appendTo="body"
              [fluid]="true"
            ></p-datepicker>
          </label>
          <label>
            <span>{{ 'purchaseOrders.builder.expectedDeliveryDate' | transloco }}</span>
            <p-datepicker
              ngSkipHydration
              formControlName="expectedDeliveryDate"
              dateFormat="dd/mm/yy"
              [showIcon]="true"
              [showButtonBar]="true"
              appendTo="body"
              [fluid]="true"
            ></p-datepicker>
          </label>
          <label>
            <span>{{ 'purchaseOrders.builder.supplierReferenceNumber' | transloco }}</span>
            <input pInputText type="text" formControlName="supplierReferenceNumber" />
          </label>
          <label class="po-builder__notes">
            <span>{{ 'purchaseOrders.builder.notes' | transloco }}</span>
            <textarea pTextarea rows="3" formControlName="notes"></textarea>
          </label>
        </form>

        <app-purchase-order-line-form (lineSubmitted)="addLine($event)" />
        <app-purchase-order-lines-table [lines]="draftState.lines()" (removeLine)="removeLine($event)" />

        @if (facade.errorMessage()) {
          <p class="po-builder__error">{{ facade.errorMessage() | transloco }}</p>
        }

        <footer class="po-builder__actions">
          <button
            pButton
            type="button"
            severity="secondary"
            icon="pi pi-trash"
            [label]="'purchaseOrders.builder.discard' | transloco"
            (click)="discardDraft()"
          ></button>
          <button
            pButton
            type="button"
            icon="pi pi-save"
            [label]="'purchaseOrders.builder.saveDraft' | transloco"
            [disabled]="facade.isSubmitting()"
            (click)="saveDraft()"
          ></button>
          @if (purchaseOrderId) {
            <button
              pButton
              type="button"
              severity="success"
              icon="pi pi-send"
              [label]="'purchaseOrders.actions.placeOrder' | transloco"
              [disabled]="facade.isSubmitting()"
              (click)="placeOrder()"
            ></button>
          }
        </footer>
      </div>
    </section>
  `,
  styles: [`
    .overlay { position: fixed; inset: 0; z-index: 60; }
    .overlay-backdrop {
      position: absolute; inset: 0;
      background: radial-gradient(circle at top right, rgba(255, 190, 120, 0.35), rgba(22, 18, 12, 0.5));
      backdrop-filter: blur(2px);
    }
    .overlay-card {
      position: relative; margin: 2vh auto;
      width: min(1120px, calc(100vw - 2rem));
      max-height: 96vh; overflow-y: auto;
      border-radius: 1rem; border: 1px solid rgba(251, 191, 36, 0.35);
      background: #fffdf8; padding: 1.25rem;
      box-shadow: 0 24px 50px rgba(24, 24, 27, 0.2);
      display: grid; gap: 1rem;
    }
    .po-builder__header { display: flex; align-items: flex-start; justify-content: space-between; gap: 1rem; }
    .po-builder__actions { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
    .po-builder__form { display: grid; gap: .75rem; grid-template-columns: repeat(4, minmax(0, 1fr)); }
    label { display: grid; gap: .35rem; font-size: .875rem; font-weight: 700; color: #1f2937; }
    .po-builder__notes { grid-column: 1 / -1; }
    .po-builder__notes textarea { width: 100%; resize: vertical; }
    .po-builder__error { color: #b42318; margin: 0; }
    .eyebrow { margin: 0; font-size: 0.7rem; letter-spacing: 0.14em; text-transform: uppercase; color: #b45309; }
    h2 { margin: 0.3rem 0 0; font-size: 1.4rem; color: #1c1917; }
    .autosave-hint { margin: 0.25rem 0 0; color: #57534e; font-size: 0.92rem; }
    .close-button { width: 2.5rem; height: 2.5rem; border: 0; border-radius: 999px; background: transparent; color: #57534e; cursor: pointer; }
    .close-button:hover { background: #fff7ed; color: #c2410c; }
    .po-builder__form .p-datepicker,
    .po-builder__form .p-autocomplete {
      width: 100%;
    }
    .po-builder__form .p-inputtext,
    .po-builder__form .p-autocomplete-input,
    .po-builder__form .p-datepicker-input,
    .po-builder__form input[pInputText],
    .po-builder__form textarea[pTextarea] {
      width: 100%;
      min-height: 2.75rem;
      border: 1px solid #cbd5e1;
      border-radius: 0.75rem;
      background: #ffffff;
      color: #111827;
      padding: 0.65rem 0.85rem;
      box-shadow: 0 1px 2px rgba(15, 23, 42, 0.06);
      transition: border-color 160ms ease, box-shadow 160ms ease;
    }
    .po-builder__form .p-inputtext:enabled:focus,
    .po-builder__form .p-autocomplete-input:enabled:focus,
    .po-builder__form .p-datepicker-input:enabled:focus,
    .po-builder__form input[pInputText]:focus,
    .po-builder__form textarea[pTextarea]:focus {
      border-color: #ea580c;
      box-shadow: 0 0 0 3px rgba(234, 88, 12, 0.16);
      outline: 0;
    }
    .po-builder__form .p-datepicker:has(.p-datepicker-dropdown) .p-datepicker-input {
      border-top-right-radius: 0;
      border-bottom-right-radius: 0;
    }
    .po-builder__form .p-datepicker-dropdown {
      min-width: 2.75rem;
      border: 1px solid #cbd5e1;
      border-left: 0;
      border-radius: 0 0.75rem 0.75rem 0;
      background: #fff7ed;
      color: #c2410c;
    }
    @media (max-width: 860px) { .po-builder__form { grid-template-columns: 1fr; } }
  `],
})
export class PurchaseOrderBuilderPageComponent implements OnInit, OnDestroy {
  private readonly authService = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly suppliersFacade = inject(SuppliersFacade);
  private readonly permissions = inject(ShopPermissionsService);

  protected readonly draftState = inject(PurchaseOrderDraftStateService);
  protected readonly facade = inject(PurchaseOrdersFacade);

  @Input() purchaseOrderId: string | null = null;
  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.fb.group({
    supplierName: [''],
    orderDate: [null as Date | null],
    expectedDeliveryDate: [null as Date | null],
    supplierReferenceNumber: [''],
    notes: [''],
  });

  protected readonly supplierSuggestions = computed(() =>
    this.suppliersFacade.suppliers().filter((supplier) => supplier.isActive && !supplier.isSystem)
  );
  protected readonly supplierNameSuggestions = signal<string[]>([]);

  private autosaveTimer: ReturnType<typeof setTimeout> | null = null;
  private clearLocalDraftAfterCreate = false;

  constructor() {
    effect(() => {
      const order = this.facade.selectedOrder();
      if (order && this.purchaseOrderId === order.purchaseOrderId) {
        if (order.status !== 'Draft') {
          this.closeRequested.emit();
          return;
        }
        if (this.draftState.hasRestoredLocalDraft() && this.draftState.restoredPurchaseOrderId() === order.purchaseOrderId) {
          untracked(() => this.patchHeaderForm());
          return;
        }
        this.draftState.replaceFromServer(order);
        untracked(() => this.patchHeaderForm());
      }
    });
    effect(() => {
      const supplierId = this.draftState.header().supplier?.id;
      if (!supplierId) return;

      const supplier = this.supplierSuggestions().find((candidate) => candidate.supplierId === supplierId);
      if (!supplier) return;

      this.draftState.resolveSupplierName(supplier.supplierId, supplier.name);
      untracked(() => this.patchHeaderForm());
    });
    effect(() => {
      this.supplierNameSuggestions.set(this.filterSupplierNames(this.form.controls.supplierName.value ?? ''));
    });
    effect(() => {
      const order = this.facade.selectedOrder();
      if (!this.clearLocalDraftAfterCreate || !order || this.purchaseOrderId) return;
      this.clearLocalDraftAfterCreate = false;
      void this.draftState.clearDraft(this.activeShopId()).then(() =>
        this.router.navigate(['/inventory/purchase-orders', order.purchaseOrderId])
      );
    });
    effect(() => {
      if (this.clearLocalDraftAfterCreate && this.facade.errorMessage()) {
        this.clearLocalDraftAfterCreate = false;
      }
    });
  }

  async ngOnInit(): Promise<void> {
    if (!this.permissions.canManagePurchaseOrders()) {
      this.closeRequested.emit();
      return;
    }

    this.suppliersFacade.load();
    const shopId = this.activeShopId();
    await this.draftState.loadDraft(shopId, this.purchaseOrderId);
    this.patchHeaderForm();

    if (this.purchaseOrderId) {
      this.facade.loadDetail(this.purchaseOrderId);
    }

    this.form.valueChanges.subscribe(() => this.scheduleAutosave());
  }

  ngOnDestroy(): void {
    if (this.autosaveTimer) clearTimeout(this.autosaveTimer);
    this.facade.clearDetail();
  }

  addLine(line: CreatePurchaseOrderLineRequest): void {
    void this.draftState.addOrMergeLine(this.activeShopId(), line);
  }

  removeLine(itemId: string): void {
    void this.draftState.removeLine(this.activeShopId(), itemId);
  }

  protected onSupplierSearch(_event: AutoCompleteCompleteEvent): void {
    this.supplierNameSuggestions.set(this.filterSupplierNames(_event.query));
  }

  saveDraft(): void {
    const payload = this.draftState.toPayload();
    if (this.purchaseOrderId) {
      this.facade.updateDraft(this.purchaseOrderId, payload);
    } else {
      this.clearLocalDraftAfterCreate = true;
      this.facade.createDraft(payload);
    }
  }

  placeOrder(): void {
    if (!this.purchaseOrderId) {
      return;
    }

    this.facade.placeOrder(this.purchaseOrderId);
  }

  async discardDraft(): Promise<void> {
    await this.draftState.clearDraft(this.activeShopId());
    this.closeRequested.emit();
  }

  private scheduleAutosave(): void {
    if (this.autosaveTimer) clearTimeout(this.autosaveTimer);
    this.autosaveTimer = setTimeout(() => {
      const raw = this.form.getRawValue();
      const supplier = this.resolveSupplier(raw.supplierName ?? '');
      void this.draftState.updateHeader(this.activeShopId(), {
        purchaseOrderId: this.purchaseOrderId,
        supplier,
        orderDate: this.toIsoDateOrNull(raw.orderDate),
        expectedDeliveryDate: this.toIsoDateOrNull(raw.expectedDeliveryDate),
        supplierReferenceNumber: raw.supplierReferenceNumber || null,
        notes: raw.notes || null,
      });
    }, 250);
  }

  private patchHeaderForm(): void {
    const header = this.draftState.header();
    const supplierName = header.supplier?.name || this.supplierSuggestions().find((supplier) => supplier.supplierId === header.supplier?.id)?.name || '';
    this.form.patchValue({
      supplierName,
      orderDate: header.orderDate ? parseDateOnlyAsLocalDate(header.orderDate) : null,
      expectedDeliveryDate: header.expectedDeliveryDate ? parseDateOnlyAsLocalDate(header.expectedDeliveryDate) : null,
      supplierReferenceNumber: header.supplierReferenceNumber ?? '',
      notes: header.notes ?? '',
    }, { emitEvent: false });
  }

  private toIsoDateOrNull(value: Date | string | null | undefined): string | null {
    if (!value) {
      return null;
    }

    if (typeof value === 'string') {
      return value;
    }

    return formatLocalIsoDate(value);
  }

  private resolveSupplier(name: string): { readonly id: string; readonly name: string } | null {
    const supplier = this.supplierSuggestions().find((candidate) => candidate.name === name);
    if (supplier) {
      return { id: supplier.supplierId, name: supplier.name };
    }

    const current = this.draftState.header().supplier;
    if (current && (name === current.name || (!name && !current.name))) {
      return current;
    }

    return null;
  }

  private filterSupplierNames(query: string): string[] {
    const normalizedQuery = query.trim().toLowerCase();
    return this.supplierSuggestions()
      .filter((supplier) => !normalizedQuery || supplier.name.toLowerCase().includes(normalizedQuery))
      .map((supplier) => supplier.name)
      .slice(0, 20);
  }

  private activeShopId(): string {
    return this.authService.session()?.activeShopId ?? '';
  }
}
