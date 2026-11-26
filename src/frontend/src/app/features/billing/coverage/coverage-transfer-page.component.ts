import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { isUuid } from '../checkout/checkout.models';
import { formatBillingInstant } from '../status/subscription-status.models';
import {
  CoverageTransferDrafts, CoverageTransferService, type CoveredShop, type TransferDecision, type TransferIntent,
  type TransferItem, type TransferPreview, type TransferPurpose, type TransferScope, type TransferShopView,
  type TransferTreatment,
} from './coverage-transfer.service';

const ACKS = ['ackShop', 'ackTreatment', 'ackPermits', 'ackUnknown', 'ackBills'] as const;

/**
 * P06-07a: request a coverage transfer of one named shop to another billing account and collect the three owner
 * consents. Nothing on this page changes coverage, access or bills; a fully consented request awaits provider review.
 */
@Component({
  selector: 'app-coverage-transfer-page',
  imports: [RouterLink, TranslocoPipe],
  templateUrl: './coverage-transfer-page.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: ['section, article, fieldset { display:grid; gap:.75rem; } section { max-width:48rem; } button, select, input[type=text] { width:fit-content; } textarea { width:100%; }'],
})
export class CoverageTransferPageComponent {
  private readonly api = inject(CoverageTransferService);
  private readonly storage = inject(CoverageTransferDrafts);
  private readonly context = inject(ShopContextService);
  private readonly locale = inject(LocalizationService);
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.paramMap, { initialValue: this.route.snapshot.paramMap });

  protected readonly acks = ACKS;
  protected readonly scope = computed<TransferScope | null>(() => {
    const owner = this.context.scope(), params = this.params(), account = params.get('accountId');
    return owner && account ? {
      ...owner, account,
      term: params.get('termId') ?? '',
      target: params.get('shopId') ?? '',
      transferId: params.get('transferId') ?? '',
    } : null;
  });

  protected readonly shops = signal<CoveredShop[]>([]);
  protected readonly items = signal<TransferItem[] | null>(null);
  protected readonly owner = signal<TransferShopView | null>(null);
  protected readonly review = signal<TransferPreview | null>(null);
  protected readonly shopId = signal('');
  protected readonly destination = signal('');
  protected readonly treatment = signal<TransferTreatment>('RetainUntilPaidThrough');
  protected readonly cutover = signal('');
  protected readonly reason = signal('');
  protected readonly confirms = signal(ACKS.map(() => false));
  protected readonly draft = signal<TransferIntent | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected readonly canReview = computed(() => isUuid(this.shopId()) && isUuid(this.destination().trim())
    && (this.treatment() === 'RetainUntilPaidThrough' || !!this.cutover().trim()));
  protected readonly ready = computed(() => !!this.review() && !!this.reason().trim() && this.confirms().every(Boolean));

  constructor() {
    effect(() => {
      const s = this.scope();
      untracked(() => {
        this.items.set(null); this.owner.set(null); this.shops.set([]); this.draft.set(null); this.resetForm();
        if (s) void this.load(s);
      });
    });
  }

  protected async load(s: TransferScope) {
    this.busy.set(true); this.error.set('');
    try {
      this.draft.set(this.storage.read(s));
      if (s.transferId) {
        const row = await this.api.owner(s);
        if (s === this.scope() && row.shopId === s.target && row.transferId === s.transferId) this.owner.set(row);
        return;
      }
      const [items, shops] = await Promise.all([this.api.list(s), s.term ? this.api.coveredShops(s) : []]);
      if (s !== this.scope()) return;
      this.items.set(items); this.shops.set(shops);
    } catch { if (s === this.scope()) this.error.set('billing.unavailable'); }
    finally { if (s === this.scope()) this.busy.set(false); }
  }

  /** Step one: show the named impact, dates, permits and unknown queues before anything can be requested. */
  protected async preview() {
    const s = this.scope();
    if (!s || this.busy() || !this.canReview()) return;
    this.busy.set(true); this.error.set(''); this.review.set(null);
    try {
      const reviewed = this.treatment() === 'ReviewedAllocation';
      const row = await this.api.preview(s, {
        destinationAccountId: this.destination().trim(), shopId: this.shopId(),
        treatment: this.treatment(), cutoverAt: reviewed ? this.cutover().trim() : null,
      });
      if (s === this.scope() && row.shopId === this.shopId()) this.review.set(row);
    } catch { if (s === this.scope()) this.error.set('billing.coverageTransfer.previewFailed'); }
    finally { if (s === this.scope()) this.busy.set(false); }
  }

  /** Any change to the request inputs discards the reviewed impact and every acknowledgement. */
  protected edit(change: () => void) {
    change();
    this.review.set(null);
    this.confirms.set(ACKS.map(() => false));
  }

  protected setShop(value: string) { return () => this.shopId.set(value); }
  protected setDestination(value: string) { return () => this.destination.set(value); }
  protected setCutover(value: string) { return () => this.cutover.set(value); }
  protected setTreatment(value: string) {
    return () => this.treatment.set(value === 'ReviewedAllocation' ? 'ReviewedAllocation' : 'RetainUntilPaidThrough');
  }

  protected tick(index: number, checked: boolean) {
    this.confirms.update(flags => flags.map((v, i) => i === index ? checked : v));
  }

  protected instant(at: string | null) {
    return at ? formatBillingInstant(at, this.locale.currentLanguage(), true) : '';
  }

  protected accepted(item: TransferItem) {
    return item.consents.filter(c => c.status === 'Accepted').length;
  }

  protected status(item: TransferItem, purpose: TransferPurpose) {
    return item.consents.find(c => c.purpose === purpose)?.status ?? 'AwaitingConsent';
  }

  protected request() {
    const p = this.review();
    if (!p || !this.ready()) return;
    void this.dispatch({
      action: 'request', label: `${p.shopName} → ${p.destinationAccountId}`,
      body: {
        operationKey: crypto.randomUUID(), destinationAccountId: p.destinationAccountId, shopId: p.shopId,
        treatment: p.treatment, cutoverAt: p.cutoverAt, reason: this.reason().trim(),
        expectedSourceAffiliationId: p.sourceAffiliationId, expectedSourcePaidThroughAt: p.sourcePaidThroughAt,
        expectedSourceOwnerRevision: p.sourceOwnerRevision, expectedEvidenceDigest: p.evidenceDigest,
        acknowledgedNamedShop: true, acknowledgedRemainingAccessTreatment: true, acknowledgedExistingPermits: true,
        acknowledgedUnknownDisconnectedQueues: true, acknowledgedHistoricalBillsStay: true,
      },
    });
  }

  /** One explicit decision for exactly one purpose, bound to the revision, terms and authority revision on screen. */
  protected decide(action: TransferDecision, purpose: TransferPurpose, shown: TransferItem | TransferShopView) {
    const authority = 'authorityRevision' in shown ? shown.authorityRevision : shown.viewerAuthorityRevision;
    if (authority === null) return;
    void this.dispatch({
      action, purpose, transferId: shown.transferId, label: `${shown.shopName}: ${purpose}`,
      body: {
        operationKey: crypto.randomUUID(), expectedProposalRevision: shown.proposalRevision,
        expectedTermsDigest: shown.termsDigest, expectedAuthorityRevision: authority,
      },
    });
  }

  protected async dispatch(command: TransferIntent, retry = false) {
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
      if (s === this.scope()) { this.resetForm(); await this.load(s); }
    } catch (e) {
      if (s !== this.scope()) return;
      // Only a definite refusal releases the frozen intent; a timeout, 404 or outage keeps it for an exact retry.
      if (e instanceof HttpErrorResponse && [400, 409].includes(e.status) && e.error?.code !== 'Billing.OperationPayloadMismatch') {
        await this.storage.settle(s, command.body.operationKey);
        if (s === this.scope()) await this.load(s);
      }
      if (s === this.scope()) this.error.set('billing.unavailable');
    } finally { if (s === this.scope()) this.busy.set(false); }
  }

  private resetForm() {
    this.review.set(null); this.shopId.set(''); this.destination.set(''); this.cutover.set('');
    this.treatment.set('RetainUntilPaidThrough'); this.reason.set('');
    this.confirms.set(ACKS.map(() => false));
  }
}
