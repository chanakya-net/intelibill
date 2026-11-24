import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { formatIstExact } from '../../accounts/account-status-api.service';
import { AdminSessionService } from '../../auth/admin-session.service';
import { PaymentReviewApi, type ReviewAction, type ReviewCase, type ReviewCommand, type ReviewDetail } from './payment-review-api.service';
import { PaymentReviewDrafts } from './payment-review-drafts';

const identifier = /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i;
const rejected = new Set(['PaymentReview.InvalidDecision', 'PaymentReview.CaseVersionConflict', 'PaymentReview.RecoveryUnavailable']);

const caseLabels: Readonly<Record<string, string>> = {
  ReceivingPinMismatch: 'Receiving account, environment or order mismatch',
  CurrencyMismatch: 'Currency mismatch',
  Underpayment: 'Underpayment',
  Overpayment: 'Overpayment',
  DuplicateFunds: 'Duplicate funds',
  ReservationExpired: 'Capacity reservation expired',
  ReservationChanged: 'Reserved capacity changed before application',
  SourceChanged: 'Subscription capacity changed',
  ReservationBusy: 'Another capacity reservation was active',
  CoverageChanged: 'Coverage revision changed',
  TermUnavailable: 'Accepted period unavailable',
  CaptureTimeUnproven: 'Capture timing unavailable',
  RestorationPending: 'Access restoration pending',
  LegacyNeedsReview: 'Historical payment needs review',
  AcceptedTermsUnavailable: 'Accepted terms unavailable',
  RefundReviewPending: 'Referred for refund review',
  NoAccessDecisionRecorded: 'No-access decision recorded',
  AcceptedTermsRecovered: 'Original accepted terms recovered',
};

interface Invocation {
  readonly admin: string;
  readonly account: string;
  readonly id: string;
  readonly session: string;
  readonly current: () => boolean;
}

@Component({
  selector: 'adm-payment-review-page',
  imports: [ButtonDirective, RouterLink],
  templateUrl: './payment-review-page.component.html',
  styleUrl: '../../form-page.css',
  styles: `
    main { max-width: 52rem; }
    article, section { overflow-wrap: anywhere; }
    article { padding-block: 0.75rem; border-bottom: 1px solid var(--app-sand-400); }
    article a, dt { font-weight: 600; }
    dd { margin-block-end: 0.5rem; }
    textarea, select { inline-size: 100%; border: 1px solid var(--app-sand-400); border-radius: 0.5rem; padding: 0.5rem; }
    @media (min-width: 40rem) {
      dl { display: grid; grid-template-columns: 12rem minmax(0, 1fr); gap: 0.5rem 1rem; }
      dd { margin-block-end: 0; }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PaymentReviewPageComponent {
  private readonly api = inject(PaymentReviewApi);
  private readonly drafts = inject(PaymentReviewDrafts);
  private readonly session = inject(AdminSessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly params = toSignal(this.route.queryParamMap);
  protected readonly account = computed(() => this.params()?.get('account')?.toLowerCase() ?? '');
  protected readonly order = computed(() => this.params()?.get('order')?.toLowerCase() ?? '');
  protected readonly id = computed(() => this.params()?.get('case')?.toLowerCase() ?? '');
  protected readonly allowed = computed(() => this.session.capabilities().includes('billing'));
  protected readonly cases = signal<ReviewCase[]>([]);
  protected readonly nextCase = signal<string | null>(null);
  protected readonly detail = signal<ReviewDetail | null>(null);
  protected readonly pending = signal<ReviewCommand | null>(null);
  protected readonly ready = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly reauthenticate = signal(false);
  protected readonly instant = formatIstExact;
  protected readonly label = (code: string) => caseLabels[code] ?? code;
  protected readonly money = (amount: number, currency: string) => currency === 'INR'
    ? new Intl.NumberFormat('en-IN', { style: 'currency', currency }).format(amount / 100)
    : new Intl.NumberFormat('en-IN').format(amount) + ' minor units (' + currency + ')';
  private generation = 0;
  private revision = 0;

  constructor() {
    effect(() => {
      this.account();
      this.order();
      this.id();
      this.session.adminId();
      this.session.sessionId();
      this.allowed();
      untracked(() => this.restore());
    });
    inject(DestroyRef).onDestroy(() => this.generation++);
  }

  protected search(event: Event, account: string) {
    event.preventDefault();
    if (!identifier.test(account.trim())) {
      this.error.set('Enter a valid billing account ID.');
      return;
    }
    void this.router.navigate([], { relativeTo: this.route, queryParams: { account: account.trim() } });
  }

  protected restore() {
    this.generation++;
    this.error.set('');
    this.busy.set(false);
    this.detail.set(null);
    this.pending.set(null);
    this.ready.set(false);
    this.cases.set([]);
    this.nextCase.set(null);
    if (!identifier.test(this.account()) || this.id() && !identifier.test(this.id())) return;
    void this.run(async invocation => {
      if (invocation.id) await this.loadDetail(invocation);
      else await this.loadCases(invocation);
    });
  }

  protected nextCases() {
    const cursor = this.nextCase();
    if (cursor) void this.run(invocation => this.loadCases(invocation, cursor));
  }

  protected nextShops() {
    const shown = this.detail();
    const cursor = shown?.shops.nextCursor;
    if (!cursor) return;
    void this.run(async ({ account, id, session, current }) => {
      const page = await this.api.read(account, id, session, cursor);
      if (current() && this.detail() === shown)
        this.detail.set({ ...page, shops: { items: [...shown.shops.items, ...page.shops.items], nextCursor: page.shops.nextCursor } });
    });
  }

  protected submit(event: Event, form: HTMLFormElement) {
    event.preventDefault();
    const detail = this.detail();
    if (!detail || detail.decision || this.pending() || !this.ready()) return;
    const fields = new FormData(form);
    const action = fields.get('action') as ReviewAction;
    const command: ReviewCommand = {
      operationKey: crypto.randomUUID(), expectedVersion: detail.version, action,
      reason: String(fields.get('reason')).trim(), evidence: String(fields.get('evidence')).trim(),
      sourceHash: action === 'RecoverAcceptedTerms' ? detail.recoverySourceHash : null,
    };
    void this.run(async invocation => {
      const draft = await this.drafts.claim(this.scope(invocation), this.revision, command);
      if (!invocation.current()) return;
      this.revision = draft.revision;
      this.pending.set(draft.pending ?? null);
      if (draft.pending?.operationKey === command.operationKey) await this.send(invocation, command, false);
      else await this.loadDetail(invocation);
    });
  }

  protected retry(lookup: boolean) {
    const command = this.pending();
    if (command) void this.run(invocation => this.send(invocation, command, lookup));
  }

  private async loadCases(invocation: Invocation, after?: string) {
    const order = this.order();
    const cases = await this.api.list(invocation.account, invocation.session, identifier.test(order) ? order : '', after);
    if (!invocation.current()) return;
    this.cases.update(prior => after ? [...prior, ...cases] : cases);
    this.nextCase.set(cases.length === 40 ? cases[39].caseId : null);
  }

  private async loadDetail(invocation: Invocation) {
    const { account, id, session, current } = invocation;
    const view = await this.api.read(account, id, session);
    if (!current()) return;
    this.detail.set(view);
    const draft = await this.drafts.read(this.scope(invocation));
    if (!current()) return;
    this.revision = draft.revision;
    this.pending.set(draft.pending ?? null);
    this.ready.set(true);
  }

  private async send(invocation: Invocation, command: ReviewCommand, lookup: boolean) {
    const { account, id, session, current } = invocation;
    const result = lookup ? await this.api.recover(account, id, session, command.operationKey)
      : await this.api.decide(account, id, session, command);
    if (result.operationKey !== command.operationKey || result.caseId !== id || result.adminId !== invocation.admin)
      throw new Error('Unexpected payment review decision identity');
    await this.drafts.settle(this.scope(invocation), command.operationKey);
    if (current()) await this.loadDetail(invocation);
  }

  private scope(invocation: Invocation) {
    return { admin: invocation.admin, account: invocation.account, order: invocation.id };
  }

  private async run(work: (invocation: Invocation) => Promise<void>) {
    const admin = this.session.adminId();
    const session = this.session.sessionId();
    const account = this.account();
    const id = this.id();
    const order = this.order();
    if (!admin || !session || !this.allowed() || this.busy()) return;
    const generation = this.generation;
    const current = () => generation === this.generation && this.allowed() && this.account() === account &&
      this.id() === id && this.order() === order && this.session.adminId() === admin && this.session.sessionId() === session;
    const invocation = { admin, session, account, id, current };
    this.busy.set(true);
    this.error.set('');
    this.reauthenticate.set(false);
    try {
      await work(invocation);
    } catch (error) {
      if (current()) await this.refuse(invocation, error);
    } finally {
      if (current()) this.busy.set(false);
    }
  }

  private async refuse(invocation: Invocation, error: unknown) {
    const code = error instanceof HttpErrorResponse ? problemCode(error.error) : null;
    const pending = this.pending();
    if (pending && code && rejected.has(code)) {
      try {
        await this.drafts.settle(this.scope(invocation), pending.operationKey);
        if (invocation.current()) await this.loadDetail(invocation);
      } catch {
        if (invocation.current()) this.error.set('The saved decision could not be refreshed. Reload the case.');
        return;
      }
    }
    if (!invocation.current()) return;
    this.reauthenticate.set(code === 'ManualPayment.RecentMfaRequired');
    this.error.set(this.reauthenticate() ? 'Sign in with MFA again to review payment evidence.' :
      this.pending() ? 'Decision is unresolved. Check or retry the original operation.' :
      code && rejected.has(code) ? 'The decision was declined. Review the refreshed case before submitting.' :
      'Payment review is unavailable. Refresh the case to try again.');
  }
}
