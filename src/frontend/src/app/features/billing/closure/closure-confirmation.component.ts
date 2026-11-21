import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonDirective } from 'primeng/button';
import { AuthService } from '../../../core/auth/auth.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { formatBillingInstant } from '../status/subscription-status.models';
import { ClosureApi, type ClosureCommand, type ClosureReview, type OwnerClosureProposal } from './closure-api.service';
import { ClosureDrafts, type OwnerBoundClosureDraft } from './closure-drafts';

interface ShopIntent {
  readonly accountId: string;
  readonly proposalId: string | null;
  readonly command: ClosureCommand;
}

@Component({
  selector: 'app-closure-confirmation',
  imports: [TranslocoPipe, ButtonDirective],
  templateUrl: './closure-confirmation.component.html',
  styleUrl: './closure-confirmation.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:storage)': 'restore()' },
})
export class ClosureConfirmationComponent {
  readonly shop = input.required<string>();
  readonly closed = input(false);
  readonly mode = input.required<'shop' | 'account'>();
  private readonly api = inject(ClosureApi);
  private readonly auth = inject(AuthService);
  private readonly drafts = inject(ClosureDrafts);
  private readonly localization = inject(LocalizationService);
  private readonly scope = computed(() => {
    const user = this.auth.session();
    return user?.activeShopId === this.shop() ? JSON.stringify([user.user.id, this.shop(), this.mode()]) : null;
  });
  protected readonly review = signal<ClosureReview | null>(null);
  protected readonly proposal = signal<OwnerClosureProposal | null>(null);
  protected readonly draft = signal<OwnerBoundClosureDraft<ShopIntent> | null>(null);
  protected readonly known = signal(false);
  protected readonly queues = signal(false);
  protected readonly financial = signal(false);
  protected readonly busy = signal(false);
  protected readonly canConfirm = computed(() => {
    const draft = this.draft();
    const review = this.review();
    if (!review) return false;
    if (this.mode() === 'shop') return !draft && !this.closed() && review.closureVersion === 0;
    return this.proposal() !== null && (!draft || (draft.settled &&
      (draft.input.proposalId !== this.proposal()?.id
        || draft.input.command.ownerVersion !== review.ownerVersion
        || draft.input.command.workDigest !== review.workDigest)));
  });
  protected readonly message = signal<string | null>(null);
  private generation = 0;

  constructor() {
    effect(() => {
      this.scope();
      untracked(() => {
        this.generation++;
        this.clearReview();
        this.busy.set(false);
        this.message.set(null);
        this.restore();
      });
    });
    inject(DestroyRef).onDestroy(() => this.generation++);
  }

  protected restore() {
    const scope = this.scope();
    try {
      this.draft.set(scope ? this.drafts.read<ShopIntent>(scope) : null);
    } catch {
      this.draft.set(null);
      this.message.set('storage');
    }
  }

  protected async openReview() {
    const shop = this.shop();
    const generation = this.generation;
    if (this.busy() || !this.scope()) return;
    this.clearReview();
    this.busy.set(true);
    this.message.set(null);
    try {
      const review = await this.api.review(shop);
      if (generation !== this.generation || shop !== this.shop()) return;
      const proposal = this.mode() === 'account' ? await this.api.ownerProposal(shop) : null;
      if (generation !== this.generation || shop !== this.shop()) return;
      if (review.shopId !== shop || review.disconnectedQueuesUnknown !== true || review.financialTreatment !== 'NoFinancialChange'
        || (proposal && (proposal.accountId !== review.accountId || proposal.target.shopId !== shop
          || proposal.target.affiliationRequestId !== review.affiliationRequestId
          || proposal.target.closureVersion !== review.closureVersion))) {
        this.message.set('unavailable');
        return;
      }
      this.proposal.set(proposal);
      this.review.set(review);
    } catch (error) {
      if (generation === this.generation) this.message.set(error instanceof HttpErrorResponse && error.status === 404
        ? 'noProposal' : 'unavailable');
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }

  protected async confirm() {
    const scope = this.scope();
    const review = this.review();
    const proposalId = this.proposal()?.id ?? null;
    if (!scope || !review || !this.known() || !this.queues() || !this.financial() || this.busy() || !this.canConfirm()) return;

    const generation = this.generation;
    const operationKey = crypto.randomUUID();
    this.busy.set(true);
    try {
      const draft = await this.drafts.prepare(scope, operationKey, {
        accountId: review.accountId, proposalId, command: {
          operationKey, expectedVersion: review.closureVersion, ownerVersion: review.ownerVersion,
          workDigest: review.workDigest, reviewedKnownWork: true, consideredDeviceQueues: true,
          financialTreatment: 'NoFinancialChange',
        } satisfies ClosureCommand,
      }, previous => this.mode() === 'account' && previous.settled &&
        (previous.input.proposalId !== proposalId || previous.input.command.ownerVersion !== review.ownerVersion ||
          previous.input.command.workDigest !== review.workDigest));
      if (!this.current(scope, generation)) return;
      this.draft.set(draft);
      this.busy.set(false);
      if (draft.operationKey === operationKey) await this.retry();
    } catch {
      if (this.current(scope, generation)) {
        this.busy.set(false);
        this.message.set('storage');
      }
    }
  }

  protected async retry() {
    const scope = this.scope();
    const draft = this.draft();
    if (!scope || !draft || draft.settled || this.busy()) return;
    const shop = this.shop();
    const generation = this.generation;
    const { accountId, proposalId, command } = draft.input;
    this.busy.set(true);
    this.message.set(null);
    try {
      const matches = proposalId ? await this.submitConsent(shop, proposalId, accountId, command)
        : await this.submitClosure(shop, accountId, command);
      if (!matches) throw new Error('Closure response did not match the accepted scope');
      await this.drafts.finish(scope, draft.operationKey, true);
      if (this.current(scope, generation)) this.restore();
    } catch (error) {
      if (this.current(scope, generation)) await this.handleFailure(error, scope, generation, draft.operationKey);
    } finally {
      if (this.current(scope, generation)) this.busy.set(false);
    }
  }

  private async submitConsent(shop: string, proposal: string, account: string, command: ClosureCommand) {
    const saved = await this.api.consent(shop, proposal, command);
    return saved.id === proposal && saved.accountId === account && saved.target.shopId === shop && saved.consentRecorded;
  }

  private async submitClosure(shop: string, account: string, command: ClosureCommand) {
    const saved = await this.api.close(shop, command);
    return saved.accountId === account && saved.operationKey === command.operationKey && saved.scope === 'Shop' && saved.financialTreatment === 'NoFinancialChange'
      && saved.shops.length === 1 && saved.shops[0].shopId === shop;
  }

  private async handleFailure(error: unknown, scope: string, generation: number, key: string) {
    if (!(error instanceof HttpErrorResponse) || error.status !== 409 || error.error?.code !== 'Closure.ReviewChanged') {
      this.message.set('unknown');
      return;
    }
    try {
      await this.drafts.finish(scope, key, false);
      if (!this.current(scope, generation)) return;
      this.clearReview();
      this.restore();
      this.message.set('stale');
    } catch {
      if (this.current(scope, generation)) this.message.set('storage');
    }
  }

  private clearReview() {
    this.review.set(null);
    this.proposal.set(null);
    this.known.set(false);
    this.queues.set(false);
    this.financial.set(false);
  }

  private current(scope: string, generation: number) {
    return generation === this.generation && this.scope() === scope;
  }

  protected instant(value: string | null) {
    return formatBillingInstant(value, this.localization.currentLanguage(), true);
  }
}
