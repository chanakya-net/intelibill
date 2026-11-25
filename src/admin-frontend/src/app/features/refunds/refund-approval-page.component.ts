import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { formatIstExact } from '../accounts/account-status-api.service';
import { AdminSessionService } from '../auth/admin-session.service';
import {
  canonicalAccountId, normalizeRefundIntakeReason, REFUND_INTAKE_STATUS_META, RefundIntakeApi, type RefundIntakeItem,
} from '../payments/refunds/refund-intake-api.service';
import {
  RefundApprovalApi, sameAccess, sameInstant, type ApproveRefundCommand, type RefundApprovalPreview, type RefundApprovalReceipt,
  type RefundPreviewInput, type RefundTreatment,
} from './refund-approval-api.service';
import { RefundApprovalDrafts } from './refund-approval-drafts';
import { effectiveInstant, parseRupees, refusalMessage, RELEASING_REFUSALS, TREATMENT_CHOICES } from './refund-approval-form';

interface Invocation {
  readonly admin: string;
  readonly account: string;
  readonly request: string;
  readonly session: string;
  readonly current: () => boolean;
}

/**
 * Billing approval of one refund request: a reviewed preview of the amount and the named access effect, then one
 * frozen command that is made durable before it is sent. An unknown outcome is only ever checked or retried under
 * its original key; an answer that arrives after the admin, session, account or request changed is ignored.
 */
@Component({
  selector: 'adm-refund-approval-page',
  imports: [ButtonDirective, RouterLink],
  templateUrl: './refund-approval-page.component.html',
  styleUrl: '../form-page.css',
  styles: `
    section, dd { overflow-wrap: anywhere; }
    fieldset { border: 0; padding: 0; margin: 0; display: grid; gap: 0.5rem; }
    textarea { inline-size: 100%; border: 1px solid var(--app-sand-400); border-radius: 0.5rem; padding: 0.5rem; }
    @media (min-width: 40rem) { dl { display: grid; grid-template-columns: 14rem minmax(0, 1fr); gap: 0.5rem 1rem; } }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RefundApprovalPageComponent {
  private readonly api = inject(RefundApprovalApi);
  private readonly intake = inject(RefundIntakeApi);
  private readonly drafts = inject(RefundApprovalDrafts);
  private readonly session = inject(AdminSessionService);
  private readonly params = toSignal(inject(ActivatedRoute).paramMap);

  protected readonly account = computed(() => canonicalAccountId(this.params()?.get('account') ?? '') ?? '');
  protected readonly request = computed(() => canonicalAccountId(this.params()?.get('request') ?? '') ?? '');
  protected readonly allowed = computed(() => this.session.capabilities().includes('billing'));
  protected readonly item = signal<RefundIntakeItem | null>(null);
  protected readonly loaded = signal(false);
  protected readonly pending = signal<ApproveRefundCommand | null>(null);
  protected readonly preview = signal<RefundApprovalPreview | null>(null);
  protected readonly receipt = signal<RefundApprovalReceipt | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly reauthenticate = signal(false);
  protected readonly amount = signal('');
  protected readonly reason = signal('');
  protected readonly treatment = signal<RefundTreatment | ''>('');
  protected readonly effective = signal('');
  protected readonly confirmed = signal(false);
  protected readonly choices = TREATMENT_CHOICES;
  protected readonly instant = formatIstExact;
  protected readonly money = (paise: number) =>
    new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(paise / 100);
  protected readonly statusLabel = (status: RefundIntakeItem['status']) => REFUND_INTAKE_STATUS_META[status].label;
  protected readonly treatmentLabel = (treatment: RefundTreatment) =>
    TREATMENT_CHOICES.find(choice => choice.value === treatment)!.label;

  /** The complete, valid form input, or null while anything is missing. */
  protected readonly input = computed<RefundPreviewInput | null>(() => {
    const amountPaise = parseRupees(this.amount());
    const treatment = this.treatment();
    const effectiveAt = treatment === 'Preserve' ? null : effectiveInstant(this.effective());
    if (!amountPaise || !treatment || !normalizeRefundIntakeReason(this.reason())) return null;
    return treatment !== 'Preserve' && !effectiveAt ? null : { amountPaise, treatment, effectiveAt };
  });
  protected readonly decidable = computed(() =>
    this.item()?.status === 'Requested' && !this.pending() && !this.receipt());
  private generation = 0;
  private revision = 0;

  constructor() {
    effect(() => {
      this.account();
      this.request();
      this.session.adminId();
      this.session.sessionId();
      this.allowed();
      untracked(() => this.restore());
    });
    inject(DestroyRef).onDestroy(() => this.generation++);
  }

  /** Any edit invalidates the reviewed preview: only an exactly matching preview can be approved. */
  protected edit(field: 'amount' | 'reason' | 'effective', event: Event) {
    this[field].set((event.target as HTMLInputElement | HTMLTextAreaElement).value);
    this.unreview();
  }

  protected choose(treatment: RefundTreatment) {
    this.treatment.set(treatment);
    this.unreview();
  }

  protected confirm(event: Event) {
    this.confirmed.set((event.target as HTMLInputElement).checked);
  }

  protected review() {
    const input = this.input();
    if (!input || !this.decidable()) return;
    void this.run(async invocation => {
      const preview = await this.api.preview(invocation, input);
      if (!invocation.current() || this.input() !== input) return;
      const item = this.item();
      if (!item || preview.orderId !== item.orderId || preview.confirmationId !== item.confirmationId ||
        preview.verifiedAmountPaise !== item.paymentAmountPaise || preview.requestedAmountPaise !== item.amountPaise)
        throw new Error('Unexpected payment in refund preview');
      this.preview.set(preview);
    });
  }

  protected submit() {
    const preview = this.preview();
    const input = this.input();
    const reason = normalizeRefundIntakeReason(this.reason());
    if (!preview || !input || !reason || !this.confirmed() || !this.decidable()) return;
    const command: ApproveRefundCommand = {
      operationKey: crypto.randomUUID(), ...input, reason, expectedVersion: preview.receiptVersion,
      impactDigest: preview.impactDigest, confirmed: true, reviewedAccess: preview.access,
      reviewedPayment: { orderId: preview.orderId, confirmationId: preview.confirmationId,
        verifiedAmountPaise: preview.verifiedAmountPaise, requestedAmountPaise: preview.requestedAmountPaise,
        remainingAmountPaise: preview.remainingAmountPaise },
    };
    void this.run(async invocation => {
      // Durable before it is sent: a reload or another tab sees this exact command, never a new key.
      const draft = await this.drafts.claim(this.scope(invocation), this.revision, command);
      if (!invocation.current()) return;
      this.revision = draft.revision;
      this.pending.set(draft.pending ?? null);
      if (draft.pending?.operationKey === command.operationKey) await this.send(invocation, command, false);
    });
  }

  protected checkProvider() {
    void this.run(async invocation => {
      await this.api.recheck(invocation);
      if (invocation.current()) await this.load(invocation);
    });
  }

  protected refreshOutcome() {
    void this.run(invocation => this.load(invocation));
  }

  protected resolve(lookup: boolean) {
    const command = this.pending();
    if (command) void this.run(invocation => this.send(invocation, command, lookup));
  }

  private unreview() {
    this.preview.set(null);
    this.confirmed.set(false);
  }

  private restore() {
    this.generation++;
    for (const cleared of [this.item, this.pending, this.preview, this.receipt]) cleared.set(null);
    for (const text of [this.amount, this.reason, this.effective, this.error]) text.set('');
    this.treatment.set('');
    for (const flag of [this.loaded, this.busy, this.confirmed, this.reauthenticate]) flag.set(false);
    if (this.account() && this.request()) void this.run(invocation => this.load(invocation));
  }

  private async load(invocation: Invocation) {
    const draft = await this.drafts.read(this.scope(invocation));
    if (!invocation.current()) return;
    this.revision = draft.revision;
    this.pending.set(draft.pending ?? null);
    const listed = await this.intake.list(invocation.account, invocation.session, undefined, invocation.request);
    if (!invocation.current()) return;
    this.item.set(listed.items.find(item => item.refundRequestId === invocation.request) ?? null);
    this.loaded.set(true);
  }

  private async send(invocation: Invocation, command: ApproveRefundCommand, lookup: boolean) {
    let result: RefundApprovalReceipt;
    try {
      result = lookup ? await this.api.recover(invocation, command.operationKey)
        : await this.api.approve(invocation, command);
    } catch (error) {
      if (invocation.current()) await this.refuse(invocation, error, !lookup);
      return;
    }
    if (!invocation.current()) return;
    const payment = command.reviewedPayment;
    const item = this.item();
    if (result.orderId !== (payment?.orderId ?? item?.orderId) ||
      result.confirmationId !== (payment?.confirmationId ?? item?.confirmationId) ||
      result.verifiedAmountPaise !== (payment?.verifiedAmountPaise ?? item?.paymentAmountPaise) ||
      result.requestedAmountPaise !== (payment?.requestedAmountPaise ?? item?.amountPaise) ||
      (payment && result.remainingBeforePaise !== payment.remainingAmountPaise) ||
      (command.reviewedAccess && !sameAccess(result.access, command.reviewedAccess)) ||
      !sameInstant(result.access.effectiveAt, command.effectiveAt) ||
      result.reason !== command.reason || result.receiptVersion !== command.expectedVersion + 1)
      throw new Error('Unexpected reviewed refund receipt');
    if (result.operationKey !== command.operationKey || result.adminId !== invocation.admin
      || result.approvedAmountPaise !== command.amountPaise || result.access.treatment !== command.treatment)
      throw new Error('Unexpected refund approval identity');
    if (!invocation.current()) return;
    await this.drafts.settle(this.scope(invocation), command.operationKey);
    if (!invocation.current()) return;
    this.revision++;
    this.pending.set(null);
    this.receipt.set(result);
    this.item.update(item => item?.status === 'Requested' ? { ...item, status: result.status, approvedAmountPaise: result.approvedAmountPaise,
      providerState: item.paymentSource === 'Online' ? 'Reserved' : 'Manual',
      accessDisposition: item.paymentSource === 'Online' ? 'AwaitingConfirmation' : 'Applied' } : item);
  }

  private scope(invocation: Invocation) {
    return { admin: invocation.admin, account: invocation.account, order: invocation.request };
  }

  private async run(work: (invocation: Invocation) => Promise<void>) {
    const admin = this.session.adminId();
    const session = this.session.sessionId();
    const account = this.account();
    const request = this.request();
    if (!admin || !session || !this.allowed() || this.busy()) return;
    const generation = this.generation;
    const current = () => generation === this.generation && this.allowed() && this.account() === account
      && this.request() === request && this.session.adminId() === admin && this.session.sessionId() === session;
    const invocation = { admin, session, account, request, current };
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

  private async refuse(invocation: Invocation, error: unknown, approvalResponse = false) {
    const code = error instanceof HttpErrorResponse ? problemCode(error.error) : null;
    const pending = this.pending();
    // A typed refusal under the frozen key proves nothing was recorded: release it and require a new preview.
    if (approvalResponse && pending && code && RELEASING_REFUSALS.has(code)) {
      try {
        await this.drafts.settle(this.scope(invocation), pending.operationKey);
      } catch {
        if (invocation.current()) this.error.set('The saved approval could not be released. Reload this request.');
        return;
      }
      if (!invocation.current()) return;
      this.revision++;
      this.pending.set(null);
    }
    if (!invocation.current()) return;
    this.unreview();
    this.reauthenticate.set(code === 'ManualPayment.RecentMfaRequired');
    const remaining = error instanceof HttpErrorResponse ? error.error?.remainingAmountPaise : null;
    const balance = code === 'Refund.AmountExceedsRemaining' && Number.isSafeInteger(remaining) && remaining >= 0
      ? ` Remaining refundable: ${this.money(remaining)}.` : '';
    this.error.set(refusalMessage(code, !!this.pending(), !!pending) + balance);
    if (code === 'Refund.AlreadyDecided' && this.item()?.status === 'Requested')
      this.item.set({ ...this.item()!, status: 'Pending' });
  }
}
