import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { formatIstExact } from '../accounts/account-status-api.service';
import { AdminSessionService } from '../auth/admin-session.service';
import { normalizeReviewReason } from './coverage-exception-api.service';
import { TransferDrafts, TransferReviewApi, type TransferCommand, type TransferReceipt, type TransferReview,
  type TransferSummary } from './transfer-review-api';

@Component({
  selector: 'adm-transfer-review',
  imports: [ButtonDirective, RouterLink],
  templateUrl: './transfer-review-page.component.html',
  styleUrl: '../form-page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TransferReviewPageComponent {
  private readonly api = inject(TransferReviewApi);
  private readonly drafts = inject(TransferDrafts);
  private readonly session = inject(AdminSessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.paramMap, { initialValue: this.route.snapshot.paramMap });
  private readonly query = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });
  protected readonly account = computed(() => this.params().get('accountId') ?? '');
  protected readonly id = computed(() => this.query().get('transfer') ?? '');
  protected readonly allowed = computed(() => this.session.capabilities().includes('licensing-manager'));
  protected readonly items = signal<TransferSummary[]>([]);
  protected readonly review = signal<TransferReview | null>(null);
  protected readonly pending = signal<TransferCommand | null>(null);
  protected readonly receipt = signal<TransferReceipt | null>(null);
  protected readonly error = signal('');
  protected readonly reason = signal('');
  protected readonly confirmed = signal(false);
  protected readonly busy = signal(false);
  protected readonly instant = formatIstExact;
  protected label(value: string | null | undefined) {
    return value?.replace(/([a-z])([A-Z])/g, '$1 $2') ?? '';
  }
  protected readonly canApprove = computed(() => !!this.review() && !this.review()?.blocker && !this.review()?.approvalState
    && !this.pending() && !this.receipt() && !this.busy() && this.confirmed() && !!normalizeReviewReason(this.reason()));
  private revision = 0;
  private generation = 0;

  constructor() {
    effect(onCleanup => {
      const account = this.account(), id = this.id(), admin = this.session.adminId(), session = this.session.sessionId();
      const allowed = this.allowed();
      const generation = ++this.generation;
      onCleanup(() => this.generation++);
      untracked(() => {
        this.reset();
        if (allowed && admin && session && account) void this.load(generation, admin, session, account, id);
      });
    });
  }

  private reset() {
    this.items.set([]); this.review.set(null); this.pending.set(null); this.receipt.set(null);
    this.error.set(''); this.reason.set(''); this.confirmed.set(false); this.busy.set(false);
  }

  private async load(gen: number, admin: string, session: string, account: string, id: string) {
    this.busy.set(true);
    try {
      if (id) {
        const draft = await this.drafts.read({ admin, account, order: id });
        if (gen !== this.generation) return;
        this.pending.set(draft.pending ?? null);
        this.revision = draft.revision;
      }
      const [rows, review] = await Promise.allSettled([
        this.api.list(account, session),
        id ? this.api.preview(account, id, session) : Promise.resolve(null),
      ]);
      if (gen !== this.generation) return;
      if (rows.status === 'fulfilled') this.items.set(rows.value);
      if (review.status === 'fulfilled') this.review.set(review.value);
      if (review.status === 'rejected') this.error.set('Transfer review could not be loaded.');
      else if (rows.status === 'rejected') this.error.set('Transfer requests could not be listed.');
    } catch {
      if (gen === this.generation) this.error.set('Transfer review could not be loaded.');
    } finally {
      if (gen === this.generation) this.busy.set(false);
    }
  }

  protected async submit(mode: 'approve' | 'lookup' | 'retry') {
    const admin = this.session.adminId(), session = this.session.sessionId(), account = this.account(), id = this.id();
    if (!this.allowed() || this.busy() || !admin || !session || !id) return;
    const gen = this.generation, scope = { admin, account, order: id };
    const current = () => gen === this.generation && account === this.account() && id === this.id()
      && admin === this.session.adminId() && session === this.session.sessionId() && this.allowed();
    const canApprove = this.canApprove();
    this.busy.set(true); this.error.set('');
    try {
      let command = this.pending();
      if (mode === 'approve') {
        const p = this.review();
        if (!canApprove || !p) return;
        const operationKey = crypto.randomUUID();
        const draft = await this.drafts.claim(scope, this.revision, {
          operationKey, reason: this.reason().trim(), expectedReviewDigest: p.reviewDigest,
          treatment: p.treatment, cutoverAt: p.cutoverAt, paidThroughAt: p.sourcePaidThroughAt,
          acknowledgedAllocation: true, acknowledgedPermitsAndUnknownQueues: true, acknowledgedHistoricalBillsStay: true,
        });
        if (!current()) return;
        command = draft.pending ?? null;
        this.pending.set(command);
        if (command?.operationKey !== operationKey) return;
      }
      if (!command) return;
      const receipt = await this.api.decide(account, id, session, command, mode === 'lookup');
      if (!current()) return;
      this.receipt.set(receipt);
      if (receipt.state === 'Applied' || receipt.state === 'NeedsReview') {
        await this.drafts.settle(scope, command.operationKey);
        if (current()) this.pending.set(null);
      }
    } catch (e) {
      if (!current()) return;
      const code = e instanceof HttpErrorResponse ? problemCode(e.error) : null;
      this.error.set(code === 'Coverage.ExceptionRecentMfaRequired' ? 'Sign in again with MFA to check this decision.'
        : code === 'Coverage.TransferChanged' ? 'The transfer terms changed. Check the saved decision before reviewing a new proposal.'
        : 'Outcome unconfirmed. Check the saved decision or retry the same operation.');
    } finally {
      if (current()) this.busy.set(false);
    }
  }
}
