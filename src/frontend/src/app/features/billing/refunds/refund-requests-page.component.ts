import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { LocaleMoneyPipe } from '../../../shared/pipes/locale-money.pipe';
import { isUuid, type CheckoutScope } from '../checkout/checkout.models';
import type { PaymentConfirmation } from '../payments/payment-confirmation.models';
import { PaymentConfirmationService } from '../payments/payment-confirmation.service';
import { formatBillingInstant } from '../status/subscription-status.models';
import { InvalidRefundRequestDraft, RefundRequestDrafts } from './refund-request-drafts';
import { RefundRequestListState } from './refund-request-list-state';
import {
  formatPaiseAsInput, mapRefundErrorKey, normalizeRefundReason,
  parseInrToPaise, REFUND_MAX_REASON_LENGTH, refundRefusalKind, refundStatusCopy,
  type CreateRefundRequest, type RefundRequestItem,
} from './refund-request.models';
import { RefundRequestService } from './refund-request.service';
import { findRefundTargetPayment } from './refund-target-payment';

@Component({
  selector: 'app-refund-requests-page',
  imports: [FormsModule, RouterLink, TranslocoPipe, LocaleMoneyPipe],
  templateUrl: './refund-requests-page.component.html',
  styles: [`
    :host { display: block; }
    .refunds-page { display: grid; gap: 1rem; max-width: 56rem; }
    .refunds-page__form { display: grid; gap: 0.75rem; max-width: 34rem; }
    .refunds-page__list { display: grid; gap: 0.75rem; list-style: none; margin: 0; padding: 0; }
    .refunds-page__card { display: grid; gap: 0.35rem; overflow-wrap: anywhere; }
    .refunds-page__meta { display: grid; grid-template-columns: max-content minmax(0, 1fr); gap: 0.25rem 1rem; margin: 0; }
    .refunds-page__meta dd { margin: 0; overflow-wrap: anywhere; }
    textarea { inline-size: 100%; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RefundRequestsPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly context = inject(ShopContextService);
  private readonly api = inject(RefundRequestService);
  private readonly confirmations = inject(PaymentConfirmationService);
  private readonly drafts = inject(RefundRequestDrafts);
  private readonly locale = inject(LocalizationService);
  private readonly routeParams = toSignal(this.route.paramMap);
  private readonly queryParams = toSignal(this.route.queryParamMap);

  protected readonly accountId = computed(() => this.routeParams()?.get('accountId') ?? '');
  protected readonly orderId = computed(() => {
    const id = this.queryParams()?.get('order') ?? '';
    return isUuid(id) ? id : null;
  });
  protected readonly confirmationId = computed(() => {
    const id = this.queryParams()?.get('confirmation') ?? '';
    return isUuid(id) ? id : null;
  });
  private readonly scope = computed<CheckoutScope | null>(() => {
    const owner = this.context.scope();
    const account = this.accountId();
    return owner && isUuid(account) ? { userId: owner.userId, shopId: owner.shopId, account } : null;
  });

  private readonly listState = new RefundRequestListState(
    (scope, after) => this.api.list(scope, after),
    error => this.mapError(error, false),
  );
  protected readonly maxReasonLength = REFUND_MAX_REASON_LENGTH;
  protected readonly items = this.listState.items;
  protected readonly nextCursor = this.listState.nextCursor;
  protected readonly listLoaded = this.listState.loaded;
  protected readonly listErrorKey = this.listState.errorKey;
  protected readonly payment = signal<PaymentConfirmation | null>(null);
  protected readonly pendingDraft = signal<CreateRefundRequest | null>(null);
  protected readonly pendingConflict = signal(false);
  protected readonly unreadableDraft = signal(false);
  protected readonly amountInput = signal('');
  protected readonly reasonInput = signal('');
  protected readonly loading = this.listState.loading;
  protected readonly submitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly submittedNotice = signal(false);
  protected readonly displayErrorKey = computed(() => this.errorKey() ?? this.listErrorKey());
  private generation = 0;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.generation++;
      this.listState.reset();
    });
    effect(() => {
      const scope = this.scope();
      const order = this.orderId();
      const confirmation = this.confirmationId();
      untracked(() => void this.load(scope, order, confirmation));
    });
  }

  protected statusLabel(item: RefundRequestItem): string {
    return refundStatusCopy(item).label;
  }

  protected statusDetail(item: RefundRequestItem): string {
    return refundStatusCopy(item).detail;
  }

  protected approvedAmountLabel(item: RefundRequestItem): string {
    return item.providerState === 'Confirmed' || item.status === 'Confirmed'
      ? 'billing.refunds.confirmedAmount' : 'billing.refunds.approvedAmount';
  }

  protected rupees(paise: number): number {
    return paise / 100;
  }

  protected instant(value: string): string {
    return formatBillingInstant(value, this.locale.currentLanguage(), true) ?? value;
  }

  protected formLocked(): boolean {
    return this.submitting() || this.pendingDraft() !== null || this.unreadableDraft();
  }

  protected canSubmit(): boolean {
    if (this.formLocked()) return false;
    const payment = this.payment();
    if (!payment) return false;
    const paise = parseInrToPaise(this.amountInput());
    return paise !== null && paise <= payment.amountPaise && normalizeRefundReason(this.reasonInput()) !== null;
  }

  protected async submit() {
    const scope = this.scope();
    const payment = this.payment();
    const paise = parseInrToPaise(this.amountInput());
    const reason = normalizeRefundReason(this.reasonInput());
    if (!scope || !payment || paise === null || paise > payment.amountPaise || !reason || this.formLocked()) {
      this.errorKey.set('billing.refunds.errors.invalid');
      return;
    }
    const generation = this.generation;
    const current = () => generation === this.generation;
    const command: CreateRefundRequest = {
      operationKey: crypto.randomUUID(),
      orderId: payment.orderId,
      confirmationId: payment.confirmationId,
      amountPaise: paise,
      reason,
    };
    this.errorKey.set(null);
    this.submittedNotice.set(false);
    try {
      const prepared = await this.drafts.prepare(scope, command);
      if (!current()) return;
      if (prepared.existing && !prepared.matchesRequest) {
        this.bindPendingDraft(prepared.draft.command, true);
        return;
      }
      this.bindPendingDraft(prepared.draft.command, false);
      await this.dispatch(scope, prepared.draft.command, current);
    } catch (error) {
      if (current()) this.handleError(error);
    }
  }

  protected async retryPending() {
    const scope = this.scope();
    const draft = this.pendingDraft();
    const payment = this.payment();
    // A saved command is only ever resent against the receipt this member can currently read.
    if (!scope || !draft || this.submitting() || !payment ||
      payment.orderId !== draft.orderId || payment.confirmationId !== draft.confirmationId) return;
    const generation = this.generation;
    await this.dispatch(scope, draft, () => generation === this.generation);
  }

  protected async clearUnreadableDraft() {
    const scope = this.scope();
    const verified = this.payment();
    if (!scope || !verified || !this.unreadableDraft()) return;
    const generation = this.generation;
    await this.drafts.discardUnreadable(scope, verified.confirmationId, verified.orderId);
    if (generation !== this.generation) return;
    this.unreadableDraft.set(false);
    this.errorKey.set(null);
    this.amountInput.set(formatPaiseAsInput(verified.amountPaise));
  }

  protected async loadMore() {
    const scope = this.scope();
    if (!scope) return;
    const generation = this.generation;
    await this.listState.loadMore(scope, () => generation === this.generation);
  }

  private async load(scope: CheckoutScope | null, order: string | null, confirmation: string | null) {
    const generation = ++this.generation;
    const current = () => generation === this.generation;
    this.resetState();
    if (!scope) return;
    await Promise.all([
      this.listState.refresh(scope, current),
      order && confirmation ? this.loadTarget(scope, order, confirmation, current) : Promise.resolve(),
    ]);
  }

  private resetState() {
    this.listState.reset();
    this.payment.set(null);
    this.pendingDraft.set(null);
    this.pendingConflict.set(false);
    this.unreadableDraft.set(false);
    this.amountInput.set('');
    this.reasonInput.set('');
    this.submitting.set(false);
    this.errorKey.set(null);
    this.submittedNotice.set(false);
  }

  /**
   * Saved commands stay untouched in storage, hidden and unsent, until the member's current read of the
   * receipt succeeds for this scope, order and confirmation.
   */
  private async loadTarget(scope: CheckoutScope, order: string, confirmation: string, current: () => boolean) {
    try {
      const lookup = await findRefundTargetPayment(this.confirmations, scope, order, confirmation, current);
      if (lookup.kind === 'stale' || !current()) return;
      if (lookup.kind !== 'found') {
        this.errorKey.set(lookup.kind === 'missing'
          ? 'billing.refunds.errors.notFound' : 'billing.refunds.errors.unavailable');
        return;
      }
      const saved = this.bindVerifiedTarget(scope, lookup.payment);
      if (saved) await this.dispatch(scope, saved, current);
    } catch (error) {
      if (current()) this.handleError(error);
    }
  }

  /** Opens the form for a verified receipt and returns the saved command to resend for it, if any. */
  private bindVerifiedTarget(scope: CheckoutScope, payment: PaymentConfirmation): CreateRefundRequest | null {
    this.payment.set(payment);
    const saved = this.drafts.read(scope, payment.confirmationId, payment.orderId);
    if (!saved) {
      this.amountInput.set(formatPaiseAsInput(payment.amountPaise));
      return null;
    }
    this.bindPendingDraft(saved.command, false);
    return saved.command;
  }

  private bindPendingDraft(command: CreateRefundRequest, conflict: boolean) {
    this.pendingDraft.set(command);
    this.pendingConflict.set(conflict);
    this.amountInput.set(formatPaiseAsInput(command.amountPaise));
    this.reasonInput.set(command.reason);
  }

  private async dispatch(scope: CheckoutScope, command: CreateRefundRequest, current: () => boolean) {
    this.submitting.set(true);
    this.errorKey.set(null);
    try {
      if (!await this.drafts.beginDelivery(scope, command)) {
        // Another tab settled this command; show what the server now holds instead of sending again.
        if (!current()) return;
        this.clearPendingDraft();
        await this.listState.refresh(scope, current);
        return;
      }
      const created = await this.api.create(scope, command);
      await this.drafts.release(scope, command);
      if (!current()) return;
      this.clearPendingDraft();
      this.submittedNotice.set(true);
      this.listState.acknowledge(created);
    } catch (error) {
      const outcome = await this.releaseAfterRefusal(scope, command, error).catch((failure: unknown) => failure);
      if (!current()) return;
      if (outcome === true) this.clearPendingDraft(false);
      this.handleError(typeof outcome === 'boolean' ? error : outcome);
    } finally {
      if (current()) this.submitting.set(false);
    }
  }

  /** Gives the key up only when the refusal proves that no delivery of it committed. */
  private async releaseAfterRefusal(scope: CheckoutScope, command: CreateRefundRequest, error: unknown) {
    const http = error instanceof HttpErrorResponse ? error : null;
    if (!http) return false;
    const code = typeof http.error?.code === 'string' ? http.error.code : null;
    const kind = refundRefusalKind(http.status, code);
    return kind !== 'inconclusive' && await this.drafts.release(scope, command, kind === 'soleDelivery');
  }

  private clearPendingDraft(resetForm = true) {
    this.pendingDraft.set(null);
    this.pendingConflict.set(false);
    if (!resetForm) return;
    this.reasonInput.set('');
    const verified = this.payment();
    if (verified) this.amountInput.set(formatPaiseAsInput(verified.amountPaise));
  }

  private handleError(error: unknown) {
    if (error instanceof InvalidRefundRequestDraft) {
      // Storage no longer holds a readable command for this receipt; nothing saved is shown or sent.
      this.clearPendingDraft(this.pendingDraft() !== null);
      this.unreadableDraft.set(true);
      return;
    }
    this.errorKey.set(this.mapError(error, this.pendingDraft() !== null));
  }

  private mapError(error: unknown, hasPending: boolean): string {
    const http = error instanceof HttpErrorResponse ? error : null;
    const code = typeof http?.error?.code === 'string' ? http.error.code : null;
    return mapRefundErrorKey(http?.status ?? null, code, hasPending);
  }
}
