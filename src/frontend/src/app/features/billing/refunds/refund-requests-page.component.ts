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
import { CONFIRMATION_PAGE_SIZE, type PaymentConfirmation } from '../payments/payment-confirmation.models';
import { PaymentConfirmationService } from '../payments/payment-confirmation.service';
import { formatBillingInstant } from '../status/subscription-status.models';
import { InvalidRefundRequestDraft, RefundRequestDrafts } from './refund-request-drafts';
import {
  formatPaiseAsInput, normalizeRefundReason, parseInrToPaise, REFUND_MAX_REASON_LENGTH, REFUND_STATUS_COPY,
  type CreateRefundRequest, type RefundRequestItem, type RefundRequestStatus,
} from './refund-request.models';
import { RefundRequestService } from './refund-request.service';

const errorCodeMap: Readonly<Record<string, string>> = {
  'Billing.RefundRequestInvalid': 'invalid',
  'Billing.RefundAmountInvalid': 'amountInvalid',
  'Billing.RefundPaymentNotFound': 'notFound',
  'Billing.OperationPayloadMismatch': 'mismatch',
};

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

  protected readonly maxReasonLength = REFUND_MAX_REASON_LENGTH;
  protected readonly items = signal<readonly RefundRequestItem[]>([]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly payment = signal<PaymentConfirmation | null>(null);
  protected readonly pendingDraft = signal<CreateRefundRequest | null>(null);
  protected readonly amountInput = signal('');
  protected readonly reasonInput = signal('');
  protected readonly loading = signal(false);
  protected readonly submitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly submittedNotice = signal(false);
  private generation = 0;

  constructor() {
    inject(DestroyRef).onDestroy(() => { this.generation++; });
    effect(() => {
      const scope = this.scope();
      const order = this.orderId();
      const confirmation = this.confirmationId();
      untracked(() => void this.load(scope, order, confirmation));
    });
  }

  protected statusLabel(status: RefundRequestStatus): string {
    return REFUND_STATUS_COPY[status].label;
  }

  protected statusDetail(status: RefundRequestStatus): string {
    return REFUND_STATUS_COPY[status].detail;
  }

  protected rupees(paise: number): number {
    return paise / 100;
  }

  protected instant(value: string): string {
    return formatBillingInstant(value, this.locale.currentLanguage(), true) ?? value;
  }

  protected canSubmit(): boolean {
    if (this.submitting() || this.pendingDraft() !== null) return false;
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
    if (!scope || !payment || paise === null || paise > payment.amountPaise || !reason || this.submitting()) {
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
      const saved = await this.drafts.prepare(scope, payment.confirmationId, command);
      if (!current()) return;
      this.pendingDraft.set(saved.command);
      await this.dispatch(scope, saved.command, current);
    } catch (error) {
      if (current()) this.handleError(error);
    }
  }

  protected async retryPending() {
    const scope = this.scope();
    const draft = this.pendingDraft();
    if (!scope || !draft || this.submitting()) return;
    const generation = this.generation;
    await this.dispatch(scope, draft, () => generation === this.generation);
  }

  protected async loadMore() {
    const scope = this.scope();
    const cursor = this.nextCursor();
    if (!scope || !cursor || this.loading()) return;
    const generation = this.generation;
    this.loading.set(true);
    this.errorKey.set(null);
    try {
      const page = await this.api.list(scope, cursor);
      if (generation !== this.generation) return;
      this.mergeItems(page.items, false);
      this.nextCursor.set(page.nextCursor);
    } catch (error) {
      if (generation === this.generation) this.handleError(error);
    } finally {
      if (generation === this.generation) this.loading.set(false);
    }
  }

  private async load(scope: CheckoutScope | null, order: string | null, confirmation: string | null) {
    const generation = ++this.generation;
    const current = () => generation === this.generation;
    this.items.set([]);
    this.nextCursor.set(null);
    this.payment.set(null);
    this.pendingDraft.set(null);
    this.errorKey.set(null);
    this.submittedNotice.set(false);
    this.submitting.set(false);
    if (!scope) return;
    this.loading.set(true);
    try {
      const page = await this.api.list(scope);
      if (!current()) return;
      this.items.set(page.items);
      this.nextCursor.set(page.nextCursor);
      if (order && confirmation) {
        await this.loadTargetPayment(scope, order, confirmation, current);
        if (!current()) return;
        const saved = this.readDraftSafely(scope, confirmation);
        if (saved) {
          this.pendingDraft.set(saved.command);
          await this.dispatch(scope, saved.command, current);
        }
      }
    } catch (error) {
      if (current()) this.handleError(error);
    } finally {
      if (current()) this.loading.set(false);
    }
  }

  private async loadTargetPayment(scope: CheckoutScope, order: string, confirmation: string, current: () => boolean) {
    let after: string | undefined;
    for (let page = 0; page < 10; page++) {
      const rows = await this.confirmations.list(scope, order, after);
      if (!current()) return;
      const match = rows.find(row => row.confirmationId === confirmation);
      if (match) {
        this.payment.set(match);
        if (!this.amountInput()) this.amountInput.set(formatPaiseAsInput(match.amountPaise));
        return;
      }
      if (rows.length < CONFIRMATION_PAGE_SIZE) break;
      after = rows.at(-1)?.confirmationId;
    }
    if (current()) this.errorKey.set('billing.refunds.errors.notFound');
  }

  private readDraftSafely(scope: CheckoutScope, confirmation: string) {
    try {
      return this.drafts.read(scope, confirmation);
    } catch (error) {
      if (error instanceof InvalidRefundRequestDraft) {
        void this.drafts.discard(scope, confirmation);
        return null;
      }
      throw error;
    }
  }

  private async dispatch(scope: CheckoutScope, command: CreateRefundRequest, current: () => boolean) {
    this.submitting.set(true);
    this.errorKey.set(null);
    try {
      const created = await this.api.create(scope, command);
      await this.drafts.discard(scope, command.confirmationId, command.operationKey);
      if (!current()) return;
      this.pendingDraft.set(null);
      this.reasonInput.set('');
      this.submittedNotice.set(true);
      this.mergeItems([created], true);
    } catch (error) {
      if (error instanceof HttpErrorResponse && [400, 403, 404, 409].includes(error.status)) {
        await this.drafts.discard(scope, command.confirmationId, command.operationKey);
        if (current()) this.pendingDraft.set(null);
      }
      if (current()) this.handleError(error);
    } finally {
      if (current()) this.submitting.set(false);
    }
  }

  private mergeItems(incoming: readonly RefundRequestItem[], prependNew: boolean) {
    const existing = this.items();
    const byId = new Map(existing.map(item => [item.refundRequestId, item]));
    for (const item of incoming) byId.set(item.refundRequestId, item);
    if (prependNew) {
      const added = incoming.filter(item => !existing.some(e => e.refundRequestId === item.refundRequestId));
      const updated = existing.map(item => byId.get(item.refundRequestId)!);
      this.items.set([...updated, ...added]);
    } else {
      this.items.set(Array.from(byId.values()));
    }
  }

  private handleError(error: unknown) {
    if (error instanceof HttpErrorResponse) {
      const code = typeof error.error?.code === 'string' ? errorCodeMap[error.error.code] : undefined;
      if (code) {
        this.errorKey.set('billing.refunds.errors.' + code);
        return;
      }
      if (error.status === 403) {
        this.errorKey.set('billing.refunds.errors.denied');
        return;
      }
    }
    this.errorKey.set(this.pendingDraft() ? 'billing.refunds.errors.unknown' : 'billing.refunds.errors.unavailable');
  }
}
