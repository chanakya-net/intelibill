import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { formatBillingInstant } from '../status/subscription-status.models';
import { CoverageReplacementService, ReplacementDrafts, type CoveredShop, type Decision, type Intent,
  type OwnerView, type Preview, type ReplacementScope, type Shop } from './coverage-replacement.service';

@Component({
  selector: 'app-coverage-replacement-page', imports: [RouterLink, TranslocoPipe],
  templateUrl: './coverage-replacement-page.component.html', changeDetection: ChangeDetectionStrategy.OnPush,
  styles: ['section, article, fieldset { display:grid; gap:.75rem; } section { max-width:44rem; } button, select { width:fit-content; }'],
})
export class CoverageReplacementPageComponent {
  private readonly api = inject(CoverageReplacementService);
  private readonly storage = inject(ReplacementDrafts);
  private readonly context = inject(ShopContextService);
  private readonly locale = inject(LocalizationService);
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.paramMap, { initialValue: this.route.snapshot.paramMap });
  protected readonly scope = computed<ReplacementScope | null>(() => {
    const owner = this.context.scope(), params = this.params(), account = params.get('accountId');
    return owner && account ? { ...owner, account, term: params.get('termId') ?? '', target: params.get('shopId') ?? '',
      replacement: params.get('replacementId') ?? '' } : null;
  });
  protected readonly preview = signal<Preview | null>(null);
  protected readonly owner = signal<OwnerView | null>(null);
  protected readonly outgoing = signal<CoveredShop | null>(null);
  protected readonly incoming = signal<Shop | null>(null);
  protected readonly draft = signal<Intent | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly confirms = signal([false, false, false]);
  protected readonly ready = computed(() => this.outgoing() && this.incoming() && this.confirms().every(Boolean));
  private page = 1;

  constructor() {
    effect(() => {
      const scope = this.scope();
      untracked(() => {
        this.preview.set(null); this.owner.set(null); this.draft.set(null); this.reset();
        if (scope) void this.load(scope);
      });
    });
  }

  protected async load(s: ReplacementScope, more = false) {
    this.busy.set(true); this.error.set('');
    try {
      this.draft.set(this.storage.read(s));
      if (s.replacement) {
        const row = await this.api.owner(s);
        if (s === this.scope() && row.shopId === s.target && row.replacementId === s.replacement) this.owner.set(row);
      } else {
        const page = more ? this.page + 1 : 1;
        const row = await this.api.read(s, page), prev = this.preview();
        if (s !== this.scope() || row.termId !== s.term) return;
        this.page = page;
        if (!more || !prev || prev.effectiveAt !== row.effectiveAt || prev.capacity !== row.capacity ||
            prev.memberAllowance !== row.memberAllowance) { this.reset(); this.preview.set(row); }
        else this.preview.set({ ...row, coveredShops: [...prev.coveredShops, ...row.coveredShops],
          eligibleShops: [...prev.eligibleShops, ...row.eligibleShops], replacements: [...prev.replacements, ...row.replacements] });
      }
    } catch { if (s === this.scope()) this.error.set('billing.unavailable'); }
    finally { if (s === this.scope()) this.busy.set(false); }
  }

  protected select(outgoing: boolean, id: string) {
    const p = this.preview();
    if (outgoing) this.outgoing.set(p?.coveredShops.find(s => s.shopId === id) ?? null);
    else this.incoming.set(p?.eligibleShops.find(s => s.shopId === id) ?? null);
    this.confirms.set([false, false, false]);
  }

  protected tick(index: number, value: boolean) { this.confirms.update(flags => flags.map((v, i) => i === index ? value : v)); }
  protected instant(at: string) { return formatBillingInstant(at, this.locale.currentLanguage(), true); }

  protected submit(action: 'schedule' | Decision) {
    const p = this.preview(), old = this.outgoing(), next = this.incoming(), owner = this.owner();
    const operationKey = crypto.randomUUID();
    if (action === 'schedule') {
      if (!p || !old || !next || !this.ready()) return;
      void this.dispatch({ action, label: old.shopName + ' → ' + next.shopName, body: { operationKey,
        outgoingShopId: old.shopId, incomingShopId: next.shopId, observedCapacity: p.capacity, observedMemberAllowance: p.memberAllowance,
        observedEffectiveAt: p.effectiveAt, observedPermitDeadline: old.permitDeadline, acknowledgedRenewal: true,
        acknowledgedPermitWait: true, acknowledgedShopSelection: true } });
    } else if (owner) void this.dispatch({ action, label: owner.shopName, body: { operationKey } });
  }

  protected async dispatch(command: Intent, retry = false) {
    const s = this.scope();
    if (!s || this.busy()) return;
    this.busy.set(true); this.error.set('');
    try {
      const saved = retry ? this.storage.read(s) : await this.storage.freeze(s, command);
      if (s !== this.scope()) return;
      this.draft.set(saved);
      if (!saved || saved.body.operationKey !== command.body.operationKey) {
        this.error.set('billing.slot.pendingConflict'); return;
      }
      await this.api.send(s, saved);
      await this.storage.settle(s, saved.body.operationKey);
      if (s === this.scope()) await this.load(s);
    } catch (e) {
      if (s !== this.scope()) return;
      if (e instanceof HttpErrorResponse && [400, 409].includes(e.status) && e.error?.code !== 'Billing.OperationPayloadMismatch') {
        await this.storage.settle(s, command.body.operationKey);
        if (s === this.scope()) await this.load(s);
      }
      if (s === this.scope()) this.error.set('billing.unavailable');
    } finally { if (s === this.scope()) this.busy.set(false); }
  }

  protected status(state: string, activation: string) {
    if (state === 'Scheduled') return 'billing.replacement.scheduled';
    if (state === 'Blocked' || activation === 'NeedsReview') return 'billing.affiliation.status.NeedsReview';
    if (state !== 'Applied') return 'billing.affiliation.status.' + state;
    if (activation === 'Waiting') return 'billing.replacement.waiting';
    if (activation === 'TermEnded') return 'billing.status.states.Expired';
    if (activation === 'Suspended') return 'billing.status.states.Suspended';
    if (activation === 'Closed') return 'billing.slot.Closed';
    return activation === 'Acknowledged' ? 'billing.slot.Acknowledged' : 'billing.slot.Applying';
  }

  private reset() { this.outgoing.set(null); this.incoming.set(null); this.confirms.set([false, false, false]); }
}
