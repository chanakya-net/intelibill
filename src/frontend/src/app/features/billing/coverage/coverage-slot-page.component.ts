import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { formatBillingInstant } from '../status/subscription-status.models';
import { CoverageSlotDrafts, CoverageSlotService, type Decision, type OwnerConsent, type SlotIntent, type SlotScope, type SlotTerm } from './coverage-slot.service';

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
  private readonly params = toSignal(this.route.paramMap, {initialValue: this.route.snapshot.paramMap});
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
        const row = await this.api.read(scope, page);
        if (scope === this.scope()) {
          if (this.page() !== page) this.reset();
          this.page.set(page);
          this.term.set(row);
        }
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
    let intent = this.draft();
    if (!intent) {
      const term = this.term(), owner = this.owner();
      if (action === 'assign' && (!this.selected() || !this.shared() || !this.free() || !term)) return;
      if (action !== 'assign' && !owner) return;
      intent = { action, operationKey: crypto.randomUUID(), shopId: owner?.assignment.shopId ?? this.selected(),
        observedCapacity: term?.capacity ?? 1, observedPaidThrough: term?.paidThrough ?? owner!.assignment.coverageEndsAt };
    }
    this.busy.set(true); this.error.set('');
    try {
      intent = await this.drafts.freeze(scope, intent);
      if (scope !== this.scope()) return;
      this.draft.set(intent);
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
    } finally {
      if (scope === this.scope()) this.busy.set(false);
    }
  }

  private reset() { this.selected.set(''); this.shared.set(false); this.free.set(false); }
}
