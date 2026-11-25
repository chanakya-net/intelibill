import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { formatBillingInstant } from '../status/subscription-status.models';
import {
  CoverageSlotDrafts, CoverageSlotService, type Assignment, type Decision, type OwnerConsent,
  type SlotIntent, type SlotScope, type SlotTerm,
} from './coverage-slot.service';

@Component({
  selector: 'app-coverage-slot-page', imports: [RouterLink, TranslocoPipe],
  templateUrl: './coverage-slot-page.component.html', changeDetection: ChangeDetectionStrategy.OnPush,
  styles: ['section, form, article { display:grid; gap:.75rem; } section { max-width:44rem; } button, select { width:fit-content; }'],
})
export class CoverageSlotPageComponent {
  private readonly api = inject(CoverageSlotService);
  private readonly drafts = inject(CoverageSlotDrafts);
  private readonly context = inject(ShopContextService);
  private readonly locale = inject(LocalizationService);
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.paramMap, { initialValue: this.route.snapshot.paramMap });
  protected readonly scope = computed<SlotScope | null>(() => {
    const owner = this.context.scope(), params = this.params();
    const account = params.get('accountId');
    return owner && account ? { ...owner, account, term: params.get('termId') ?? '', target: params.get('shopId') ?? '',
      assignment: params.get('assignmentId') ?? '' } : null;
  });
  protected readonly term = signal<SlotTerm | null>(null);
  protected readonly owner = signal<OwnerConsent | null>(null);
  protected readonly page = signal(1);
  protected readonly selected = signal('');
  protected readonly shared = signal(false);
  protected readonly free = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly draft = signal<SlotIntent | null>(null);

  constructor() {
    effect(() => {
      const scope = this.scope();
      untracked(() => {
        this.page.set(1); this.term.set(null); this.owner.set(null); this.draft.set(null); this.reset();
        if (scope) void this.load(scope);
      });
    });
  }

  protected async load(scope: SlotScope, page = this.page()) {
    this.error.set(''); this.busy.set(true);
    try {
      this.draft.set(this.drafts.read(scope));
      if (scope.assignment) {
        const row = await this.api.owner(scope);
        if (scope === this.scope()) this.owner.set(row);
      } else {
        const prev = this.term();
        const row = await this.api.read(scope, page);
        if (scope === this.scope()) this.applyTerm(prev, row, page);
      }
    } catch {
      if (scope === this.scope()) this.error.set('billing.unavailable');
    } finally {
      if (scope === this.scope()) this.busy.set(false);
    }
  }

  protected instant(value: string) { return formatBillingInstant(value, this.locale.currentLanguage(), true); }

  protected async submit(action: 'assign' | Decision) {
    const scope = this.scope();
    if (!scope || this.busy()) return;
    const command = this.buildCommand(action);
    if (!command) return;
    this.busy.set(true); this.error.set('');
    try {
      const frozen = await this.drafts.freeze(scope, command);
      if (scope !== this.scope()) return;
      this.draft.set(frozen.intent);
      if (frozen.kind === 'existing') { this.error.set('billing.slot.pendingConflict'); return; }
      await this.dispatch(scope, frozen.intent);
    } finally {
      if (scope === this.scope()) this.busy.set(false);
    }
  }

  protected async retryPending(expectedKey: string) {
    const scope = this.scope();
    if (!scope || this.busy()) return;
    const saved = this.drafts.read(scope);
    if (!saved) { this.draft.set(null); await this.load(scope); return; }
    if (saved.operationKey !== expectedKey) {
      this.draft.set(saved); this.error.set('billing.slot.pendingConflict'); return;
    }
    this.busy.set(true); this.error.set('');
    try {
      await this.dispatch(scope, saved);
    } finally {
      if (scope === this.scope()) this.busy.set(false);
    }
  }

  protected billingStatusKey(a: Assignment): string {
    if (a.state === 'Refused') return 'billing.slot.changed';
    if (a.state !== 'Applied') return 'billing.affiliation.status.' + a.state;
    return this.activationKey(a.activation);
  }

  protected ownerStatusKey(o: OwnerConsent): string {
    if (o.status === 'AppliedLocally') return 'billing.slot.AppliedLocally';
    if (o.status === 'Accepted') return 'billing.slot.Applying';
    if (o.status === 'Expired') return o.assignment.state === 'Applied' ? 'billing.status.states.Expired' : 'billing.affiliation.status.Expired';
    if (['Applying', 'Acknowledged', 'Grace', 'Suspended', 'Closed', 'NeedsReview'].includes(o.status))
      return this.activationKey(o.status);
    return 'billing.affiliation.status.' + o.status;
  }

  protected actionLabelKey(action: SlotIntent['action']): string {
    return 'billing.slot.actions.' + action;
  }

  private activationKey(status: string): string {
    switch (status) {
      case 'TermEnded': return 'billing.status.states.Expired';
      case 'Grace': return 'billing.status.states.Grace';
      case 'Suspended': return 'billing.status.states.Suspended';
      case 'Closed': return 'billing.slot.Closed';
      case 'NeedsReview': return 'billing.affiliation.status.NeedsReview';
      case 'Acknowledged': return 'billing.slot.Acknowledged';
      default: return 'billing.slot.Applying';
    }
  }

  private applyTerm(prev: SlotTerm | null, row: SlotTerm, page: number) {
    if (this.page() !== page) this.reset();
    else if (prev && (prev.paidThrough !== row.paidThrough || prev.capacity !== row.capacity)) {
      this.shared.set(false); this.free.set(false);
      if (this.selected() && !row.eligibleShops.some(s => s.shopId === this.selected())) this.selected.set('');
    }
    this.page.set(page);
    this.term.set(row);
  }

  private buildCommand(action: 'assign' | Decision): SlotIntent | null {
    const term = this.term(), owner = this.owner();
    if (action === 'assign') {
      const shop = term?.eligibleShops.find(s => s.shopId === this.selected());
      if (!shop || !this.shared() || !this.free() || !term) return null;
      return { action, operationKey: crypto.randomUUID(), shopId: shop.shopId, shopName: shop.shopName,
        observedCapacity: term.capacity, observedPaidThrough: term.paidThrough };
    }
    if (!owner) return null;
    return { action, operationKey: crypto.randomUUID(), shopId: owner.assignment.shopId, shopName: owner.assignment.shopName,
      observedCapacity: 1, observedPaidThrough: owner.assignment.coverageEndsAt };
  }

  private async dispatch(scope: SlotScope, intent: SlotIntent) {
    try {
      await this.api.submit(scope, intent);
      await this.drafts.settle(scope, intent.operationKey);
      if (scope === this.scope()) { this.reset(); await this.load(scope); }
    } catch (e) {
      if (scope !== this.scope()) return;
      if (e instanceof HttpErrorResponse && [400, 409].includes(e.status) && e.error?.code !== 'Billing.OperationPayloadMismatch') {
        await this.drafts.settle(scope, intent.operationKey);
        if (scope !== this.scope()) return;
        await this.load(scope);
        if (scope === this.scope()) this.error.set(e.error?.code === 'Coverage.MemberCapacityExceeded'
          ? 'billing.trial.reasons.MemberCap' : 'billing.slot.changed');
      } else this.error.set('billing.unavailable');
    }
  }

  private reset() { this.selected.set(''); this.shared.set(false); this.free.set(false); }
}
