import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { formatBillingInstant } from '../status/subscription-status.models';
import {
  CoverageExceptionDrafts, CoverageExceptionService, type ConsentDecision, type CoveredShop,
  type EarlyReplacementIntent, type EarlyReplacementOwnerView, type EarlyReplacementPreview,
  type EarlyReplacementScope, type EligibleShop, type OverlapDisposition,
} from './coverage-exception.service';

@Component({
  selector: 'app-coverage-exception-page',
  imports: [RouterLink, TranslocoPipe],
  templateUrl: './coverage-exception-page.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: ['section, article, fieldset { display:grid; gap:.75rem; } section { max-width:48rem; } button, select, input[type=text] { width:fit-content; } textarea { width:100%; }'],
})
export class CoverageExceptionPageComponent {
  private readonly api = inject(CoverageExceptionService);
  private readonly storage = inject(CoverageExceptionDrafts);
  private readonly context = inject(ShopContextService);
  private readonly locale = inject(LocalizationService);
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.paramMap, { initialValue: this.route.snapshot.paramMap });

  protected readonly scope = computed<EarlyReplacementScope | null>(() => {
    const owner = this.context.scope(), params = this.params(), account = params.get('accountId');
    return owner && account ? {
      ...owner, account,
      term: params.get('termId') ?? '',
      target: params.get('shopId') ?? '',
      exceptionId: params.get('exceptionId') ?? '',
    } : null;
  });

  protected readonly preview = signal<EarlyReplacementPreview | null>(null);
  protected readonly owner = signal<EarlyReplacementOwnerView | null>(null);
  protected readonly outgoing = signal<CoveredShop | null>(null);
  protected readonly incoming = signal<EligibleShop | null>(null);
  protected readonly earlyStart = signal('');
  protected readonly paidCutover = signal('');
  protected readonly disposition = signal<OverlapDisposition>('Wait');
  protected readonly reason = signal('');
  protected readonly confirms = signal([false, false, false, false]);
  protected readonly draft = signal<EarlyReplacementIntent | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  private page = 1;

  protected readonly ready = computed(() =>
    !!this.outgoing() && !!this.incoming()
    && !!this.earlyStart().trim() && !!this.paidCutover().trim()
    && !!this.reason().trim() && this.confirms().every(Boolean));

  constructor() {
    effect(() => {
      const s = this.scope();
      untracked(() => {
        this.preview.set(null); this.owner.set(null); this.draft.set(null); this.resetForm();
        if (s) void this.load(s);
      });
    });
  }

  protected async load(s: EarlyReplacementScope, more = false) {
    this.busy.set(true); this.error.set('');
    try {
      this.draft.set(this.storage.read(s));
      if (s.exceptionId) {
        const row = await this.api.owner(s);
        if (s === this.scope() && row.shopId === s.target && row.exceptionId === s.exceptionId)
          this.owner.set(row);
      } else {
        const page = more ? this.page + 1 : 1;
        const row = await this.api.read(s, page), prev = this.preview();
        if (s !== this.scope() || row.termId !== s.term) return;
        this.page = page;
        if (!more || !prev || prev.normalRenewalAt !== row.normalRenewalAt) {
          this.resetForm();
          this.earlyStart.set(row.observedAt);
          this.preview.set(row);
        } else {
          this.preview.set({
            ...row,
            coveredShops: [...prev.coveredShops, ...row.coveredShops],
            eligibleShops: [...prev.eligibleShops, ...row.eligibleShops],
            exceptions: [...prev.exceptions, ...row.exceptions],
          });
        }
      }
    } catch { if (s === this.scope()) this.error.set('billing.unavailable'); }
    finally { if (s === this.scope()) this.busy.set(false); }
  }

  protected selectShop(isOutgoing: boolean, id: string) {
    const p = this.preview();
    if (isOutgoing) {
      const shop = p?.coveredShops.find(s => s.shopId === id) ?? null;
      this.outgoing.set(shop);
      if (shop) {
        const start = p?.observedAt ?? '';
        this.earlyStart.set(start);
        this.paidCutover.set(shop.observedPermitDeadline ?? start);
        if (shop.observedPermitDeadline) this.disposition.set('CourtesyBridge');
      }
    } else {
      this.incoming.set(p?.eligibleShops.find(s => s.shopId === id) ?? null);
    }
    this.confirms.set([false, false, false, false]);
  }

  protected tick(index: number, checked: boolean) {
    this.confirms.update(flags => flags.map((v, i) => i === index ? checked : v));
  }

  protected instant(at: string | null) {
    return at ? formatBillingInstant(at, this.locale.currentLanguage(), true) : '';
  }

  protected submit(action: 'request' | ConsentDecision) {
    const p = this.preview(), old = this.outgoing(), next = this.incoming(), owner = this.owner();
    const operationKey = crypto.randomUUID();
    if (action === 'request') {
      if (!p || !old || !next || !this.ready()) return;
      void this.dispatch({
        action,
        label: `${old.shopName} → ${next.shopName}`,
        body: {
          operationKey,
          outgoingShopId: old.shopId,
          incomingShopId: next.shopId,
          requestedEarlyStartAt: this.earlyStart().trim(),
          proposedPaidCutoverAt: this.paidCutover().trim(),
          overlapDisposition: this.disposition(),
          reason: this.reason().trim(),
          observedCapacity: p.capacity,
          observedMemberAllowance: p.memberAllowance,
          observedNormalRenewalAt: p.normalRenewalAt,
          observedPermitDeadline: old.observedPermitDeadline,
          acknowledgedShopSelection: true,
          acknowledgedRemainingAccessAllocation: true,
          acknowledgedExistingPermits: true,
          acknowledgedUnknownDisconnectedQueues: true,
        },
      });
    } else if (owner) {
      void this.dispatch({ action, label: `${owner.leg}: ${owner.shopName}`, body: { operationKey } });
    }
  }

  protected async dispatch(command: EarlyReplacementIntent, retry = false) {
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

  private resetForm() {
    this.outgoing.set(null); this.incoming.set(null);
    this.earlyStart.set(''); this.paidCutover.set('');
    this.disposition.set('Wait'); this.reason.set('');
    this.confirms.set([false, false, false, false]);
  }
}
