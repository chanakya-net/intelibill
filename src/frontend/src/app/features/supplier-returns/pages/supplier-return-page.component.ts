import { injectScopedPrint } from '../../../core/context/scoped-print';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, effect, inject, signal } from '@angular/core';
import { problemCode } from '@intelibill/api';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, firstValueFrom, Observable } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { AuthService } from '../../../core/auth/auth.service';
import { ShopContextService } from '../../../core/context/shop-context.service';
import type { ShopScope } from '../../../core/context/shop-scope';
import {
  OPERATING_ACCESS_ERROR_KEYS,
  OperatingAccessHintService,
  operatingAccessErrorKey,
} from '../../billing/status/operating-access-hint.service';
import { SupplierReturnService } from '../services/supplier-return.service';
import { SupplierReturnDraftService } from '../services/supplier-return-draft.service';
import type {
  PurchaseReceiptDetail,
  SupplierReturnDetail,
  SupplierReturnAction,
  SupplierReturnDraftRecord,
  SupplierReturnEditableDraft,
  SupplierReturnMutationPayload,
  SupplierReturnRequest,
  SupplierReturnCreditRequest,
  ReceiptReversalRequest,
  GroupReceiptReversalRequest,
  ReceiptReversalResult,
} from '../services/supplier-return.models';
import {
  completionOperationType,
  guidIdentity,
  requireCompletionOutcome,
  requireCompletionResult,
} from '../services/supplier-return-outcome';
import { SupplierReturnPrintViewComponent } from '../components/supplier-return-print-view.component';
interface SourceView {
  id: string;
  group: string | null;
  lots: string;
  supplierId: string;
  credit: boolean;
  print: boolean;
}

interface CompletionAttempt {
  view: SourceView;
  scope: ShopScope;
  action: SupplierReturnAction;
  draftKey: string;
  target: string;
}

export function validShipmentQuantity(quantity: number, available: number, scale: number): boolean {
  return (
    Number.isFinite(quantity) &&
    quantity > 0 &&
    quantity <= available &&
    Math.abs(quantity * 10 ** scale - Math.round(quantity * 10 ** scale)) < 1e-7
  );
}
export function validCreditAmount(amount: unknown): boolean {
  if (typeof amount !== 'number' || !Number.isFinite(amount) || amount < 0 || amount > 99999999.99)
    return false;
  const paise = Math.round(amount * 100);
  return Number.isSafeInteger(paise) && paise / 100 === amount;
}
@Component({
  selector: 'app-supplier-return-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    TranslocoPipe,
    SupplierReturnPrintViewComponent,
    ButtonModule,
    InputTextModule,
    TextareaModule,
  ],
  templateUrl: './supplier-return-page.component.html',
  styleUrl: './supplier-return-page.component.scss',
})
export class SupplierReturnPageComponent implements OnInit {
  private readonly printCurrent = injectScopedPrint();
  private readonly destroyRef = inject(DestroyRef);
  readonly hasMoreHistory = signal(false);
  private historyPage = 1;
  private historyRevision = 0;
  private readonly api = inject(SupplierReturnService);
  private readonly drafts = inject(SupplierReturnDraftService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly context = inject(ShopContextService);
  private readonly auth = inject(AuthService);
  private readonly access = inject(OperatingAccessHintService);
  readonly lots = signal<PurchaseReceiptDetail[]>([]);
  readonly document = signal<SupplierReturnDetail | null>(null);
  readonly history = signal<SupplierReturnDetail[]>([]);
  readonly record = signal<SupplierReturnDraftRecord | null>(null);
  readonly busy = signal(false);
  readonly previewing = signal(false);
  private previewSequence = 0;
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
  readonly securityNotice = computed(() => {
    const message = this.access.blockingMessage();
    return message === OPERATING_ACCESS_ERROR_KEYS['Licence.SecuritySuspended'] ? message : null;
  });
  readonly canRecordShipment = computed(
    () =>
      this.canShip() && !this.securityNotice() && this.lots().some((lot) => this.shippable(lot)),
  );
  readonly canReverse = computed(
    () =>
      this.owner() &&
      !this.securityNotice() &&
      this.lots().length > 0 &&
      this.lots().every(
        (lot) =>
          !!lot.supplierId &&
          lot.receiptVersion > 0 &&
          lot.batchVersion > 0 &&
          !lot.reversalEligibilityReasons.length,
      ),
  );
  readonly canAcceptCredit = computed(() => {
    const document = this.document();
    return (
      this.owner() &&
      !this.securityNotice() &&
      !this.printMode &&
      document?.status === 'PendingSupplierCredit' &&
      document.version > 0 &&
      document.lines.length > 0 &&
      document.lines.every((line) => !!line.returnLineId) &&
      new Set(document.lines.map((line) => line.returnLineId)).size === document.lines.length
    );
  });
  readonly frozen = computed(() => !!this.record()?.pending);
  get printMode() {
    return this.view.print;
  }
  reason = '';
  reference = '';
  accepted = false;
  quantities: Record<string, number> = {};
  credits: Record<string, number> = {};
  selected: Record<string, boolean> = {};
  private readonly actionState = signal<SupplierReturnAction>('shipment');
  get action() {
    return this.actionState();
  }
  set action(value: SupplierReturnAction) {
    this.actionState.set(value);
  }
  filter = '';
  private scope: ShopScope | null = null;
  private view = this.readView();
  get group() {
    return this.view.group;
  }
  private get id() {
    return this.view.id;
  }
  private get key() {
    const saved = this.record();
    return saved?.pending && saved.draft.actionKind === this.action
      ? saved.targetId
      : this.draftKey(this.action, this.view);
  }

  private draftKey(action: SupplierReturnAction, view: SourceView) {
    return `${action}:${guidIdentity(view.group ?? view.id)}`;
  }

  private readView(): SourceView {
    const { paramMap, queryParamMap } = this.route.snapshot;
    const returnId = paramMap.get('returnId');
    const receiptId = paramMap.get('receiptValueId');
    return {
      id: receiptId ?? returnId ?? '',
      group: receiptId ? queryParamMap.get('group') : null,
      lots: receiptId ? (queryParamMap.get('lots') ?? '') : '',
      supplierId: queryParamMap.get('supplierId') ?? '',
      credit: returnId !== null,
      print: queryParamMap.get('print') === '1',
    };
  }

  private attempt(scope: ShopScope): CompletionAttempt {
    const view = this.view;
    return { view, scope, action: this.action, draftKey: this.key, target: view.group ?? view.id };
  }
  private storageReady = true;
  constructor() {
    effect(() => {
      if (!this.securityNotice()) return;
      this.lots.set([]);
      this.document.set(null);
      this.history.set([]);
      this.hasMoreHistory.set(false);
      this.status.set('');
    });
  }

  shippable(lot: PurchaseReceiptDetail): boolean {
    return (
      !!lot.supplierId &&
      !lot.isVoided &&
      lot.returnableQuantity > 0 &&
      lot.receiptVersion > 0 &&
      lot.batchVersion > 0
    );
  }

  async ngOnInit() {
    this.scope = this.context.scope();
    if (!this.scope) return;
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        const next = this.readView();
        const previous = this.view;
        if (JSON.stringify(next) !== JSON.stringify(previous)) void this.openView(next);
      });
    await this.openView(this.readView());
  }

  private async openView(view: SourceView) {
    this.view = view;
    this.action = view.credit ? 'credit' : 'shipment';
    this.ready.set(false);
    this.busy.set(true);
    this.previewing.set(false);
    this.record.set(null);
    this.lots.set([]);
    this.document.set(null);
    this.history.set([]);
    this.hasMoreHistory.set(false);
    this.error.set('');
    this.status.set('');
    this.reason = this.reference = '';
    this.accepted = false;
    this.quantities = {};
    this.credits = {};
    this.selected = {};
    this.storageReady = true;

    try {
      await this.access.refresh();
      await this.refreshAndRestore(view);
    } finally {
      if (this.current(view)) {
        this.ready.set(this.storageReady);
        this.busy.set(false);
      }
    }
  }

  private current(view: SourceView) {
    return (
      view === this.view &&
      !this.destroyRef.destroyed &&
      !!this.scope &&
      this.context.scope()?.epoch === this.scope.epoch &&
      this.context.isCurrent(this.scope)
    );
  }

  private async refreshAndRestore(view: SourceView) {
    if (!this.current(view)) return;
    await this.refresh(view);
    if (this.current(view)) await this.restore(view);
  }

  async refresh(view = this.view) {
    if (!this.current(view) || this.securityNotice()) return;
    try {
      if (view.credit) {
        const result = await firstValueFrom(this.api.getReturn(view.id));
        if (this.current(view) && !this.securityNotice()) this.document.set(result);
      } else if (view.id) {
        const ids = [...new Set([view.id, ...view.lots.split(',').filter(Boolean)])];
        const result = await Promise.all(ids.map((id) => firstValueFrom(this.api.getReceipt(id))));
        if (this.current(view) && !this.securityNotice()) this.applyLots(result);
      } else {
        await this.loadHistory(false);
      }
    } catch (error) {
      this.reportFailure(error, 'supplierReturns.loadError', view);
    }
  }

  private applyLots(lots: PurchaseReceiptDetail[]) {
    this.lots.set(lots);
    for (const lot of lots) {
      this.quantities[lot.receiptValueId] ??= 0;
      this.credits[lot.receiptValueId] ??= lot.suggestedCreditAmount;
      if (!this.shippable(lot)) this.selected[lot.receiptValueId] = false;
      else this.selected[lot.receiptValueId] ??= true;
    }
  }

  async loadMoreHistory() {
    if (!this.busy() && !this.securityNotice() && this.hasMoreHistory())
      await this.loadHistory(true);
  }
  private async loadHistory(append: boolean) {
    const view = this.view;
    const revision = append ? this.historyRevision : ++this.historyRevision;
    const status = this.filter;
    const supplierId = view.supplierId;
    const page = append ? this.historyPage + 1 : 1;
    if (!append) {
      this.history.set([]);
      this.hasMoreHistory.set(false);
    }
    this.busy.set(true);
    try {
      const result = await firstValueFrom(
        this.api.listReturns({
          ...(supplierId ? { supplierId } : {}),
          ...(status ? { status } : {}),
          page: String(page),
          pageSize: '20',
        }),
      );
      if (
        !this.current(view) ||
        !!this.securityNotice() ||
        revision !== this.historyRevision ||
        status !== this.filter
      )
        return;
      this.history.set(
        append
          ? [
              ...new Map(
                [...this.history(), ...result].map((value) => [value.returnId, value]),
              ).values(),
            ]
          : result,
      );
      this.historyPage = page;
      this.hasMoreHistory.set(result.length === 20);
      this.error.set('');
    } catch (error) {
      if (revision === this.historyRevision)
        this.reportFailure(error, 'supplierReturns.loadError', view);
    } finally {
      if (this.current(view) && revision === this.historyRevision) this.busy.set(false);
    }
  }
  async choose(action: 'shipment' | 'reversal') {
    if (
      this.frozen() ||
      this.busy() ||
      this.securityNotice() ||
      !this.canShip() ||
      (action === 'reversal' && !this.owner())
    )
      return;
    const view = this.view;
    this.busy.set(true);
    try {
      await this.save();
      if (!this.current(view) || this.securityNotice()) return;
      this.action = action;
      await this.restore(view);
    } finally {
      if (this.current(view)) this.busy.set(false);
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
          const lot = this.lots().find((value) => value.receiptValueId === line.receiptValueId);
          if (lot) this.selected[line.receiptValueId] = this.shippable(lot);
        } else if ('acceptedAmount' in line) this.credits[line.returnLineId] = line.acceptedAmount;
      }
  }
  private async restore(view: SourceView) {
    const scope = this.scope;
    if (!scope || !view.id) return;
    try {
      let action = this.action;
      let saved = await this.loadDraft(scope, view, action);
      if (!view.credit && !saved?.pending) {
        const other = action === 'reversal' ? 'shipment' : 'reversal';
        const alternate = await this.loadDraft(scope, view, other);
        if (alternate?.pending) {
          action = other;
          saved = alternate;
        }
      }
      if (!this.current(view)) return;
      while (saved?.confirmedOperationKey && this.current(view)) {
        await this.drafts.clearConfirmed(scope, saved.targetId, saved.confirmedOperationKey);
        saved = await this.loadDraft(scope, view, action);
      }
      if (!this.current(view)) return;
      this.action = action;
      this.record.set(saved);
      if (saved) this.applyDraft(saved.draft);
      else
        for (const line of this.document()?.lines ?? [])
          this.credits[line.returnLineId] ??= line.proposedCreditAmount;
    } catch {
      if (!this.current(view)) return;
      this.storageReady = false;
      this.error.set('supplierReturns.storageError');
      this.ready.set(false);
    }
  }

  private loadDraft(scope: ShopScope, view: SourceView, action: SupplierReturnAction) {
    return this.drafts.load(scope, this.draftKey(action, view));
  }

  async previewLot(lot: PurchaseReceiptDetail): Promise<void> {
    if (
      !this.scope ||
      this.frozen() ||
      this.busy() ||
      !this.canRecordShipment() ||
      !this.shippable(lot)
    )
      return;
    const quantity = this.quantities[lot.receiptValueId];
    if (
      !lot.supplierId ||
      !validShipmentQuantity(quantity, lot.returnableQuantity, lot.quantityScale)
    ) {
      await this.save();
      return;
    }
    const view = this.view;
    const sequence = ++this.previewSequence;
    const priorCredit = this.credits[lot.receiptValueId];
    this.previewing.set(true);
    try {
      const preview = await firstValueFrom(
        this.api.preview(this.scope, lot.supplierId, [
          { receiptValueId: lot.receiptValueId, quantity },
        ]),
      );
      if (
        !this.current(view) ||
        !!this.securityNotice() ||
        this.frozen() ||
        sequence !== this.previewSequence ||
        this.quantities[lot.receiptValueId] !== quantity
      )
        return;
      const fresh = preview.lines[0];
      this.lots.update((lots) =>
        lots.map((value) => (value.receiptValueId === fresh.receiptValueId ? fresh : value)),
      );
      if (this.credits[lot.receiptValueId] === priorCredit)
        this.credits[lot.receiptValueId] = fresh.suggestedCreditAmount;
      await this.save();
    } catch (error) {
      this.reportFailure(error, 'supplierReturns.loadError', view);
    } finally {
      if (this.current(view) && sequence === this.previewSequence) this.previewing.set(false);
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
      !!this.securityNotice() ||
      this.frozen() ||
      !this.id ||
      (!this.lots().length && !this.document())
    )
      return Promise.resolve();
    const view = this.view;
    const scope = this.scope;
    const key = this.key;
    const draft = this.editable();
    this.saveQueue = this.saveQueue.then(async () => {
      if (!this.current(view) || this.key !== key) return;
      try {
        const saved = await this.drafts.save(scope, key, draft, this.record()?.revision ?? 0);
        if (this.current(view) && this.key === key) this.record.set(saved);
      } catch {
        if (this.current(view)) this.error.set('supplierReturns.storageError');
      }
    });
    return this.saveQueue;
  }

  valid() {
    if (this.previewing() || this.frozen() || this.securityNotice()) return false;
    if (this.reason.trim().length < 3 || this.reason.trim().length > 255) return false;
    if (this.action === 'credit')
      return (
        this.canAcceptCredit() &&
        this.document()!.lines.every((x) => validCreditAmount(this.credits[x.returnLineId]))
      );
    if (this.action === 'reversal') return this.canReverse();
    const selected = this.lots().filter((x) => this.selected[x.receiptValueId]);
    return (
      this.canRecordShipment() &&
      selected.length > 0 &&
      new Set(selected.map((x) => x.supplierId)).size === 1 &&
      selected.every(
        (x) =>
          this.shippable(x) &&
          validShipmentQuantity(
            this.quantities[x.receiptValueId],
            x.returnableQuantity,
            x.quantityScale,
          ) &&
          validCreditAmount(this.credits[x.receiptValueId]),
      )
    );
  }
  async submit() {
    if (!this.scope || this.busy() || this.printMode || !this.valid() || !this.ready()) return;
    const attempt = this.attempt(this.scope);
    const { view, scope, draftKey } = attempt;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.saveQueue;
      if (!this.current(view) || !this.valid()) return;
      const prepared = await this.drafts.prepare(
        scope,
        draftKey,
        this.editable(),
        this.record()?.revision ?? 0,
      );
      if (!this.current(view)) return;
      this.record.set(prepared);
      this.applyDraft(prepared.draft);
      if (prepared.pending?.status === 'sending') await this.send(attempt, prepared, false);
    } catch {
      if (this.current(view)) this.error.set('supplierReturns.storageError');
    } finally {
      if (this.current(view)) this.busy.set(false);
    }
  }

  async recover() {
    const pending = this.record()?.pending;
    const scope = this.scope;
    if (!pending || !scope || this.busy() || !this.canShip() || this.printMode) return;
    const attempt = this.attempt(scope);
    const { view, draftKey } = attempt;
    const { operationKey, payload } = pending;
    this.busy.set(true);
    this.error.set('');
    try {
      if (this.securityNotice()) await this.access.refresh();
      if (!this.canRecover(view) || (await this.restoreAfterConfirmation(attempt, operationKey)))
        return;
      if (!this.canRecover(view)) return;

      const claimed = await this.drafts.claimRecovery(scope, draftKey, operationKey, payload);
      if (!this.canRecover(view)) return;
      this.record.set(claimed);
      const result = await this.lookup(attempt, claimed.pending!);
      if (!this.canRecover(view)) return;
      if (result) {
        await this.confirm(attempt, operationKey, result);
      } else if (result === null && !(await this.restoreAfterConfirmation(attempt, operationKey))) {
        await this.send(attempt, claimed, true);
      }
    } catch {
      const confirmed = await this.restoreAfterConfirmation(attempt, operationKey).catch(
        () => false,
      );
      if (!confirmed && this.current(view)) this.error.set('supplierReturns.storageError');
    } finally {
      if (this.current(view)) this.busy.set(false);
    }
  }

  private canRecover(view: SourceView) {
    return this.current(view) && !this.securityNotice() && this.canShip();
  }

  private async lookup(
    attempt: CompletionAttempt,
    pending: NonNullable<SupplierReturnDraftRecord['pending']>,
  ) {
    const { scope, action, target, view } = attempt;
    try {
      const lookup = await firstValueFrom(
        this.api.getOperation(scope, completionOperationType(action), pending.operationKey),
      );
      return requireCompletionOutcome(lookup, action, target, pending.payload);
    } catch (error) {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 404 &&
        problemCode(error.error) === 'Operation.NotFound'
      )
        return null;
      this.reportFailure(error, 'supplierReturns.unknown', view);
      return undefined;
    }
  }

  private async restoreAfterConfirmation(attempt: CompletionAttempt, key: string) {
    const { scope, draftKey, view } = attempt;
    if (!(await this.drafts.isConfirmed(scope, draftKey, key))) return false;
    if (this.current(view)) {
      this.record.set(null);
      await this.refreshAndRestore(view);
    }
    return true;
  }

  private reportFailure(error: unknown, fallback: string, view: SourceView) {
    if (!this.current(view)) return;
    const licenceError = operatingAccessErrorKey(error);
    const sourceIneligible =
      error instanceof HttpErrorResponse &&
      problemCode(error.error) === 'SupplierReturn.SourceIneligible';
    this.access.reportRefusal(error, this.scope);
    this.error.set(
      this.securityNotice()
        ? ''
        : (licenceError ?? (sourceIneligible ? 'supplierReturns.sourceIneligible' : fallback)),
    );
  }

  async refreshAccess() {
    if (this.busy()) return;
    const view = this.view;
    this.busy.set(true);
    try {
      await this.access.refresh();
      if (this.current(view) && !this.securityNotice()) await this.refreshAndRestore(view);
    } finally {
      if (this.current(view)) this.busy.set(false);
    }
  }

  private async send(
    attempt: CompletionAttempt,
    record: SupplierReturnDraftRecord,
    recovery: boolean,
  ) {
    const { view, scope, action, draftKey, target } = attempt;
    const { operationKey, payload } = record.pending!;
    if (!this.canRecover(view) || view.print) return;
    const requiresOwner =
      action !== 'shipment' || ('creditAccepted' in payload && payload.creditAccepted);
    if (requiresOwner && !this.owner()) return;
    try {
      const result = await firstValueFrom(this.mutate(attempt, payload));
      await this.confirm(
        attempt,
        operationKey,
        requireCompletionResult(result, action, target, payload),
      );
    } catch (error) {
      const settled = await this.drafts.settleFailure(
        scope,
        draftKey,
        operationKey,
        error,
        recovery,
      );
      if (this.current(view)) {
        this.record.set(settled.record);
        this.reportFailure(
          error,
          settled.outcome === 'unknown' ? 'supplierReturns.unknown' : 'supplierReturns.rejected',
          view,
        );
        if (settled.outcome === 'rejected') await this.refresh(view);
      }
    }
  }

  private mutate(
    attempt: CompletionAttempt,
    payload: SupplierReturnMutationPayload,
  ): Observable<SupplierReturnDetail | ReceiptReversalResult> {
    const { scope, action, target, view } = attempt;
    if (action === 'shipment') return this.api.record(scope, payload as SupplierReturnRequest);
    if (action === 'credit')
      return this.api.acceptCredit(scope, target, payload as SupplierReturnCreditRequest);
    return view.group
      ? this.api.reversePurchaseOrderReceipt(scope, target, payload as GroupReceiptReversalRequest)
      : this.api.reverseReceipt(scope, target, payload as ReceiptReversalRequest);
  }

  private async confirm(
    attempt: CompletionAttempt,
    key: string,
    result: SupplierReturnDetail | ReceiptReversalResult,
  ) {
    const { scope, draftKey, view, action } = attempt;
    await this.drafts.settle(scope, draftKey, key, 'confirmed');
    await this.drafts.clearConfirmed(scope, draftKey, key);
    if (!this.current(view)) return;
    this.record.set(null);
    this.status.set('supplierReturns.saved');
    if ('returnId' in result && action === 'shipment') {
      await this.router.navigate(['/supplier-returns', result.returnId]);
    } else await this.refreshAndRestore(view);
  }
  print() {
    this.printCurrent();
  }
}
