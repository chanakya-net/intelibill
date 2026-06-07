import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, effect, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';

import { AuthService } from '../../../core/auth/auth.service';
import { SuppliersFacade } from '../../suppliers/state/suppliers.facade';
import { PurchaseOrderLineFormComponent } from '../components/purchase-order-line-form.component';
import { PurchaseOrderLinesTableComponent } from '../components/purchase-order-lines-table.component';
import { CreatePurchaseOrderLineRequest } from '../services/purchase-order.service';
import { PurchaseOrderDraftStateService } from '../services/purchase-order-draft-state.service';
import { PurchaseOrdersFacade } from '../state/purchase-orders.facade';

@Component({
  selector: 'app-purchase-order-builder-page',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    PurchaseOrderLineFormComponent,
    PurchaseOrderLinesTableComponent,
  ],
  template: `
    <section class="po-builder">
      <header class="po-builder__header">
        <div>
          <h1>{{ (purchaseOrderId ? 'purchaseOrders.editPo' : 'purchaseOrders.newPo') | transloco }}</h1>
          @if (draftState.loadingDraft()) {
            <p>{{ 'purchaseOrders.autosaveLoaded' | transloco }}</p>
          }
        </div>
        <a routerLink="/inventory/purchase-orders">{{ 'purchaseOrders.actions.cancel' | transloco }}</a>
      </header>

      <form class="po-builder__form" [formGroup]="form">
        <label>
          <span>{{ 'purchaseOrders.supplier' | transloco }}</span>
          <input type="text" formControlName="supplierName" list="po-suppliers" />
          <datalist id="po-suppliers">
            @for (supplier of supplierSuggestions(); track supplier.supplierId) {
              <option [value]="supplier.name"></option>
            }
          </datalist>
        </label>
        <label>
          <span>Order date</span>
          <input type="date" formControlName="orderDate" />
        </label>
        <label>
          <span>Expected delivery</span>
          <input type="date" formControlName="expectedDeliveryDate" />
        </label>
        <label>
          <span>Supplier ref</span>
          <input type="text" formControlName="supplierReferenceNumber" />
        </label>
        <label class="po-builder__notes">
          <span>{{ 'purchaseOrders.notes' | transloco }}</span>
          <textarea rows="3" formControlName="notes"></textarea>
        </label>
      </form>

      <app-purchase-order-line-form (lineSubmitted)="addLine($event)" />
      <app-purchase-order-lines-table [lines]="draftState.lines()" (removeLine)="removeLine($event)" />

      @if (facade.errorMessage()) {
        <p class="po-builder__error">{{ facade.errorMessage() | transloco }}</p>
      }

      <footer class="po-builder__actions">
        <button type="button" (click)="discardDraft()">{{ 'purchaseOrders.actions.cancel' | transloco }}</button>
        <button type="button" [disabled]="facade.isSubmitting()" (click)="saveDraft()">
          {{ 'purchaseOrders.actions.saveDraft' | transloco }}
        </button>
      </footer>
    </section>
  `,
  styles: [`
    .po-builder { display: grid; gap: 1rem; padding: 1rem; max-width: 1120px; margin: 0 auto; }
    .po-builder__header, .po-builder__actions { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
    .po-builder__form { display: grid; gap: .75rem; grid-template-columns: repeat(4, minmax(0, 1fr)); }
    label { display: grid; gap: .25rem; font-size: .875rem; }
    input, textarea { min-height: 2.25rem; padding: .35rem .5rem; }
    .po-builder__notes { grid-column: 1 / -1; }
    .po-builder__error { color: #b42318; }
    @media (max-width: 860px) { .po-builder__form { grid-template-columns: 1fr; } }
  `],
})
export class PurchaseOrderBuilderPageComponent implements OnInit, OnDestroy {
  private readonly authService = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly suppliersFacade = inject(SuppliersFacade);

  protected readonly draftState = inject(PurchaseOrderDraftStateService);
  protected readonly facade = inject(PurchaseOrdersFacade);

  protected readonly purchaseOrderId = this.route.snapshot.paramMap.get('purchaseOrderId');
  protected readonly supplierSuggestions = computed(() =>
    this.suppliersFacade.suppliers().filter((supplier) => supplier.isActive && !supplier.isSystem)
  );

  readonly form = this.fb.group({
    supplierName: [''],
    orderDate: [''],
    expectedDeliveryDate: [''],
    supplierReferenceNumber: [''],
    notes: [''],
  });

  private autosaveTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    effect(() => {
      const order = this.facade.selectedOrder();
      if (order && this.purchaseOrderId === order.purchaseOrderId) {
        void this.draftState.replaceFromServer(this.activeShopId(), order);
        this.patchHeaderForm();
      }
    });
  }

  async ngOnInit(): Promise<void> {
    this.suppliersFacade.load();
    const shopId = this.activeShopId();
    await this.draftState.loadDraft(shopId);
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

  removeLine(description: string): void {
    void this.draftState.removeLine(this.activeShopId(), description);
  }

  saveDraft(): void {
    const payload = this.draftState.toPayload();
    if (this.purchaseOrderId) {
      this.facade.updateDraft(this.purchaseOrderId, payload);
    } else {
      this.facade.createDraft(payload);
    }
    void this.draftState.clearDraft(this.activeShopId());
  }

  async discardDraft(): Promise<void> {
    await this.draftState.clearDraft(this.activeShopId());
    await this.router.navigate(['/inventory/purchase-orders']);
  }

  private scheduleAutosave(): void {
    if (this.autosaveTimer) clearTimeout(this.autosaveTimer);
    this.autosaveTimer = setTimeout(() => {
      const raw = this.form.getRawValue();
      const supplier = this.supplierSuggestions().find((candidate) => candidate.name === raw.supplierName) ?? null;
      void this.draftState.updateHeader(this.activeShopId(), {
        purchaseOrderId: this.purchaseOrderId,
        supplier: supplier ? { id: supplier.supplierId, name: supplier.name } : null,
        orderDate: raw.orderDate || null,
        expectedDeliveryDate: raw.expectedDeliveryDate || null,
        supplierReferenceNumber: raw.supplierReferenceNumber || null,
        notes: raw.notes || null,
      });
    }, 250);
  }

  private patchHeaderForm(): void {
    const header = this.draftState.header();
    this.form.patchValue({
      supplierName: header.supplier?.name ?? '',
      orderDate: header.orderDate ?? '',
      expectedDeliveryDate: header.expectedDeliveryDate ?? '',
      supplierReferenceNumber: header.supplierReferenceNumber ?? '',
      notes: header.notes ?? '',
    }, { emitEvent: false });
  }

  private activeShopId(): string {
    return this.authService.session()?.activeShopId ?? '';
  }
}
