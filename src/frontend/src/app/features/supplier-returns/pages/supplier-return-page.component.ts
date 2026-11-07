import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom, Observable } from 'rxjs';
import { TranslocoPipe } from '@ngneat/transloco';
import { AuthService } from '../../../core/auth/auth.service';
import { ShopContextService } from '../../../core/context/shop-context.service';
import type { ShopScope } from '../../../core/context/shop-scope';
import { SupplierReturnService } from '../services/supplier-return.service';
import { SupplierReturnDraftService } from '../services/supplier-return-draft.service';
import type {
  PurchaseReceiptDetail,
  SupplierReturnDetail,
  SupplierReturnDraftRecord,
  SupplierReturnEditableDraft,
  SupplierReturnMutationPayload,
  SupplierReturnRequest,
  SupplierReturnCreditRequest,
  ReceiptReversalRequest,
  GroupReceiptReversalRequest,
  ReceiptReversalResult,
} from '../services/supplier-return.models';
import { SupplierReturnPrintViewComponent } from '../components/supplier-return-print-view.component';
export function validShipmentQuantity(quantity: number, available: number, scale: number): boolean {
  return (
    Number.isFinite(quantity) &&
    quantity > 0 &&
    quantity <= available &&
    Math.abs(quantity * 10 ** scale - Math.round(quantity * 10 ** scale)) < 1e-7
  );
}
@Component({
  selector: 'app-supplier-return-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, TranslocoPipe, SupplierReturnPrintViewComponent],
  templateUrl: './supplier-return-page.component.html',
  styleUrl: './supplier-return-page.component.scss',
})
export class SupplierReturnPageComponent implements OnInit {
  private readonly api = inject(SupplierReturnService);
  private readonly drafts = inject(SupplierReturnDraftService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly context = inject(ShopContextService);
  private readonly auth = inject(AuthService);
  readonly lots = signal<PurchaseReceiptDetail[]>([]);
  readonly document = signal<SupplierReturnDetail | null>(null);
  readonly history = signal<SupplierReturnDetail[]>([]);
  readonly record = signal<SupplierReturnDraftRecord | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly status = signal('');
  readonly ready = signal(false);
  readonly owner = computed(() => {
    const session = this.auth.session();
    return (
      session?.shops.find((x) => x.shopId === session.activeShopId)?.role.toLowerCase() === 'owner'
    );
  });
  readonly canShip = computed(() => {
    const session = this.auth.session();
    const role = session?.shops.find((x) => x.shopId === session.activeShopId)?.role.toLowerCase();
    return role === 'owner' || role === 'manager';
  });
  readonly frozen = computed(() => !!this.record()?.pending);
  readonly printMode = this.route.snapshot.queryParamMap.get('print') === '1';
  reason = '';
  reference = '';
  accepted = false;
  quantities: Record<string, number> = {};
  credits: Record<string, number> = {};
  selected: Record<string, boolean> = {};
  private readonly actionState = signal<'shipment' | 'credit' | 'reversal'>('shipment');
  get action() {
    return this.actionState();
  }
  set action(value: 'shipment' | 'credit' | 'reversal') {
    this.actionState.set(value);
  }
  filter = '';
  private scope: ShopScope | null = null;
  private target = '';
  group = this.route.snapshot.queryParamMap.get('group');
  private get id() {
    return (
      this.route.snapshot.paramMap.get('receiptValueId') ??
      this.route.snapshot.paramMap.get('returnId') ??
      ''
    );
  }
  private get key() {
    return `${this.action}:${this.group ?? this.id}`;
  }
  async ngOnInit() {
    this.scope = this.context.scope();
    if (!this.scope) return;
    this.action = this.route.snapshot.paramMap.has('returnId') ? 'credit' : 'shipment';
    this.target = this.group ?? this.id;
    await this.refresh();
    await this.restore();
  }
  private current() {
    return (
      !!this.scope &&
      this.context.scope()?.epoch === this.scope.epoch &&
      this.context.isCurrent(this.scope)
    );
  }
  async refresh() {
    this.busy.set(true);
    try {
      if (this.route.snapshot.paramMap.has('returnId')) {
        const result = await firstValueFrom(this.api.getReturn(this.id));
        if (this.current()) this.document.set(result);
      } else if (this.id) {
        const ids = [
          ...new Set([
            this.id,
            ...(this.route.snapshot.queryParamMap.get('lots') ?? '').split(',').filter(Boolean),
          ]),
        ];
        const result = await Promise.all(ids.map((id) => firstValueFrom(this.api.getReceipt(id))));
        if (this.current()) {
          this.lots.set(result);
          for (const lot of result) {
            this.quantities[lot.receiptValueId] ??= 0;
            this.credits[lot.receiptValueId] ??= lot.suggestedCreditAmount;
            this.selected[lot.receiptValueId] ??= true;
          }
        }
      } else {
        const supplierId = this.route.snapshot.queryParamMap.get('supplierId') ?? '';
        const result = await firstValueFrom(
          this.api.listReturns({
            ...(supplierId ? { supplierId } : {}),
            ...(this.filter ? { status: this.filter } : {}),
          }),
        );
        if (this.current()) this.history.set(result);
      }
    } catch {
      if (this.current()) this.error.set('supplierReturns.loadError');
    } finally {
      if (this.current()) {
        this.busy.set(false);
        this.ready.set(true);
      }
    }
  }
  async choose(action: 'shipment' | 'reversal') {
    if (this.frozen() || this.busy()) return;
    this.busy.set(true);
    try {
      await this.save();
      this.action = action;
      await this.restore();
    } finally {
      this.busy.set(false);
    }
  }
  private applyDraft(draft: SupplierReturnEditableDraft) {
    const p = draft.payload;
    this.reason = p.reason;
    this.reference = 'supplierReference' in p ? (p.supplierReference ?? '') : '';
    if ('creditAccepted' in p) {
      this.accepted = p.creditAccepted;
      for (const lot of this.lots()) this.selected[lot.receiptValueId] = false;
    }
    if ('lines' in p)
      for (const line of p.lines) {
        if ('quantity' in line) {
          this.quantities[line.receiptValueId] = line.quantity;
          this.credits[line.receiptValueId] = line.proposedCreditAmount;
          this.selected[line.receiptValueId] = true;
        } else if ('acceptedAmount' in line) this.credits[line.returnLineId] = line.acceptedAmount;
      }
  }
  private async restore() {
    if (!this.scope) return;
    try {
      if (!this.document() && this.id) {
        for (const kind of ['shipment', 'reversal'] as const) {
          const saved = await this.drafts.load(this.scope, `${kind}:${this.target}`);
          if (saved?.pending) {
            this.action = kind;
            break;
          }
        }
      }
      let saved = await this.drafts.load(this.scope, this.key);
      if (saved?.confirmedOperationKey) {
        await this.drafts.clearConfirmed(this.scope, this.key, saved.confirmedOperationKey);
        saved = null;
      }
      if (!this.current()) return;
      this.record.set(saved);
      if (saved) this.applyDraft(saved.draft);
      else if (this.document())
        for (const line of this.document()!.lines)
          this.credits[line.returnLineId] = line.proposedCreditAmount;
    } catch {
      this.error.set('supplierReturns.storageError');
      this.ready.set(false);
    }
  }
  private editable(): SupplierReturnEditableDraft {
    if (this.action === 'credit')
      return {
        actionKind: 'credit',
        payload: {
          expectedVersion: this.document()!.version,
          reason: this.reason,
          supplierReference: this.reference || null,
          lines: this.document()!.lines.map((x) => ({
            returnLineId: x.returnLineId,
            acceptedAmount: this.credits[x.returnLineId] ?? 0,
          })),
        },
      };
    if (this.action === 'reversal')
      return {
        actionKind: 'reversal',
        payload: this.group
          ? {
              reason: this.reason,
              lines: this.lots().map((x) => ({
                receiptValueId: x.receiptValueId,
                expectedReceiptVersion: x.receiptVersion,
                expectedBatchVersion: x.batchVersion,
              })),
            }
          : {
              reason: this.reason,
              expectedReceiptVersion: this.lots()[0].receiptVersion,
              expectedBatchVersion: this.lots()[0].batchVersion,
            },
      };
    return {
      actionKind: 'shipment',
      payload: {
        supplierId: this.lots()[0].supplierId!,
        reason: this.reason,
        creditAccepted: this.owner() && this.accepted,
        supplierReference: this.reference || null,
        lines: this.lots()
          .filter((x) => this.selected[x.receiptValueId])
          .map((x) => ({
            receiptValueId: x.receiptValueId,
            expectedReceiptVersion: x.receiptVersion,
            expectedBatchVersion: x.batchVersion,
            quantity: this.quantities[x.receiptValueId] ?? 0,
            proposedCreditAmount: this.credits[x.receiptValueId] ?? 0,
          })),
      },
    };
  }
  private saveQueue: Promise<void> = Promise.resolve();
  save(): Promise<void> {
    if (
      !this.scope ||
      !this.ready() ||
      this.frozen() ||
      !this.id ||
      (!this.lots().length && !this.document())
    )
      return Promise.resolve();
    const scope = this.scope;
    const key = this.key;
    const draft = this.editable();
    this.saveQueue = this.saveQueue.then(async () => {
      try {
        const saved = await this.drafts.save(scope, key, draft, this.record()?.revision ?? 0);
        if (this.current() && this.key === key) this.record.set(saved);
      } catch {
        if (this.current()) this.error.set('supplierReturns.storageError');
      }
    });
    return this.saveQueue;
  }

  valid() {
    if (this.reason.trim().length < 3 || this.reason.trim().length > 255) return false;
    if (this.action === 'credit')
      return (
        this.owner() &&
        this.document()?.status === 'PendingSupplierCredit' &&
        this.document()!.lines.every(
          (x) =>
            Number.isFinite(this.credits[x.returnLineId]) &&
            this.credits[x.returnLineId] >= 0 &&
            Math.round(this.credits[x.returnLineId] * 100) === this.credits[x.returnLineId] * 100,
        )
      );
    if (this.action === 'reversal')
      return this.owner() && this.lots().every((x) => !x.reversalEligibilityReasons.length);
    const selected = this.lots().filter((x) => this.selected[x.receiptValueId]);
    return (
      this.canShip() &&
      selected.length > 0 &&
      new Set(selected.map((x) => x.supplierId)).size === 1 &&
      selected.every(
        (x) =>
          !!x.supplierId &&
          validShipmentQuantity(
            this.quantities[x.receiptValueId],
            x.returnableQuantity,
            x.quantityScale,
          ) &&
          Number.isFinite(this.credits[x.receiptValueId]) &&
          this.credits[x.receiptValueId] >= 0,
      )
    );
  }
  async submit() {
    if (!this.scope || this.busy() || !this.valid() || !this.ready()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.saveQueue;
      const prepared = await this.drafts.prepare(
        this.scope,
        this.key,
        this.editable(),
        this.record()?.revision ?? 0,
      );
      if (!this.current()) return;
      this.record.set(prepared);
      await this.send(prepared, false);
    } catch {
      if (this.current()) this.error.set('supplierReturns.storageError');
    } finally {
      if (this.current()) this.busy.set(false);
    }
  }
  private slug() {
    return this.action === 'shipment'
      ? 'supplier-return'
      : this.action === 'credit'
        ? 'supplier-return-credit'
        : 'purchase-receipt-reversal';
  }
  async recover() {
    const pending = this.record()?.pending;
    if (!pending || !this.scope || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      if (await this.drafts.isConfirmed(this.scope, this.key, pending.operationKey)) {
        if (this.current()) {
          this.record.set(null);
          await this.refresh();
          await this.restore();
        }
        return;
      }
      const claimed = await this.drafts.claimRecovery(
        this.scope,
        this.key,
        pending.operationKey,
        pending.payload,
      );
      if (!this.current()) return;
      this.record.set(claimed);
      try {
        const result = await firstValueFrom(
          this.api.getOperation(this.scope, this.slug(), pending.operationKey),
        );
        await this.confirm(pending.operationKey, result.response);
        return;
      } catch {}
      if (await this.drafts.isConfirmed(this.scope, this.key, pending.operationKey)) {
        if (this.current()) {
          this.record.set(null);
          await this.refresh();
        }
        return;
      }
      await this.send(claimed, true);
    } catch {
      if (await this.drafts.isConfirmed(this.scope!, this.key, pending.operationKey)) {
        if (this.current()) {
          this.record.set(null);
          await this.refresh();
          await this.restore();
        }
      } else if (this.current()) this.error.set('supplierReturns.storageError');
    } finally {
      if (this.current()) this.busy.set(false);
    }
  }
  private async send(record: SupplierReturnDraftRecord, recovery: boolean) {
    const scope = this.scope!;
    const pending = record.pending!;
    let request: Observable<SupplierReturnDetail | ReceiptReversalResult>;
    if (this.action === 'shipment')
      request = this.api.record(scope, pending.payload as SupplierReturnRequest);
    else if (this.action === 'credit')
      request = this.api.acceptCredit(
        scope,
        this.id,
        pending.payload as SupplierReturnCreditRequest,
      );
    else
      request = this.group
        ? this.api.reversePurchaseOrderReceipt(
            scope,
            this.group,
            pending.payload as GroupReceiptReversalRequest,
          )
        : this.api.reverseReceipt(scope, this.id, pending.payload as ReceiptReversalRequest);
    try {
      const result = await firstValueFrom(request);
      await this.confirm(pending.operationKey, result);
    } catch (error) {
      const settled = await this.drafts.settleFailure(
        scope,
        this.key,
        pending.operationKey,
        error,
        recovery,
      );
      if (this.current()) {
        this.record.set(settled.record);
        this.error.set(
          settled.outcome === 'unknown' ? 'supplierReturns.unknown' : 'supplierReturns.rejected',
        );
        if (settled.outcome === 'rejected') await this.refresh();
      }
    }
  }
  private async confirm(key: string, result: SupplierReturnDetail | ReceiptReversalResult) {
    await this.drafts.settle(this.scope!, this.key, key, 'confirmed');
    await this.drafts.clearConfirmed(this.scope!, this.key, key);
    if (!this.current()) return;
    this.record.set(null);
    this.status.set('supplierReturns.saved');
    if ('returnId' in result && this.action === 'shipment') {
      await this.router.navigate(['/supplier-returns', result.returnId]);
    } else await this.refresh();
  }
  print() {
    window.print();
  }
}
