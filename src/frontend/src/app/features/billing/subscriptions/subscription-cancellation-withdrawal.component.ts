import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonDirective } from 'primeng/button';
import { AuthService } from '../../../core/auth/auth.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { formatBillingInstant, type SubscriptionTermStatus } from '../status/subscription-status.models';
import type { CancellationScope } from './subscription-cancellation-drafts';
import { SubscriptionCancellationWithdrawalDrafts, type WithdrawalDraft } from './subscription-cancellation-withdrawal-drafts';
import { SubscriptionCancellationService, type WithdrawalCommand, type WithdrawalOutcome } from './subscription-cancellation.service';

type Failure = 'storage' | 'unknown' | 'conflict' | 'effective';
const definitive: Readonly<Record<string, Failure>> = {
  'Billing.CancellationConflict': 'conflict',
  'Billing.CancellationEffective': 'effective',
  'Billing.OperationPayloadMismatch': 'conflict',
};

/**
 * Withdraws the term's scheduled cancellation after explicit confirmation. The command is frozen per
 * actor/account/term before HTTP; an unknown outcome is only checked or resent with the same key.
 */
@Component({
  selector: 'app-subscription-cancellation-withdrawal',
  imports: [ButtonDirective, TranslocoPipe],
  templateUrl: './subscription-cancellation-withdrawal.component.html',
  styles: [`
    :host { display: grid; gap: 0.75rem; }
    :host:empty { display: none; }
    button { justify-self: start; }
    section { display: grid; gap: 0.5rem; }
    p { margin: 0; }
    label { display: flex; gap: 0.5rem; align-items: baseline; }
    .actions { display: flex; flex-wrap: wrap; gap: 0.5rem; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:storage)': 'restore()' },
})
export class SubscriptionCancellationWithdrawalComponent {
  readonly account = input.required<string>();
  readonly term = input.required<SubscriptionTermStatus>();
  readonly saved = output<void>();
  private readonly api = inject(SubscriptionCancellationService);
  private readonly drafts = inject(SubscriptionCancellationWithdrawalDrafts);
  private readonly auth = inject(AuthService);
  private readonly localization = inject(LocalizationService);
  private readonly scope = computed(() => {
    const actor = this.auth.session()?.user.id;
    return actor ? { actor, account: this.account(), term: this.term().termId } : null;
  }, { equal: (left, right) => left?.actor === right?.actor && left?.account === right?.account && left?.term === right?.term });
  protected readonly reviewing = signal(false);
  protected readonly checked = signal(false);
  protected readonly waiting = signal(false);
  protected readonly draft = signal<WithdrawalDraft | null>(null);
  protected readonly error = signal<Failure | null>(null);
  /** The settled withdrawal still describes this term: no cancellation has been scheduled since. */
  protected readonly withdrawn = computed(() => {
    const draft = this.draft();
    return draft?.settled === true && !this.term().cancellation ? draft : null;
  });
  private generation = 0;

  constructor() {
    effect(() => {
      this.scope();
      untracked(() => {
        this.generation++;
        this.reviewing.set(false);
        this.checked.set(false);
        this.error.set(null);
        this.waiting.set(false);
        this.restore();
      });
    });
    inject(DestroyRef).onDestroy(() => this.generation++);
  }

  protected restore() {
    const scope = this.scope();
    try {
      this.draft.set(scope ? this.drafts.read(scope) : null);
      if (this.error() === 'storage') this.error.set(null);
    } catch {
      this.draft.set(null);
      this.error.set('storage');
    }
  }

  /**
   * A new withdrawal may start before the cancellation takes effect, unless an unknown one is outstanding or this
   * schedule was already withdrawn. The server judges the exact boundary; an expired term is not offered.
   */
  protected canStart() {
    const change = this.term().cancellation;
    const draft = this.draft();
    return !!change && this.term().state !== 'Expired' && (!draft || (draft.settled && draft.command.currentVersion < change.version));
  }

  protected async confirm() {
    const scope = this.scope();
    const change = this.term().cancellation;
    if (!scope || !change || !this.checked() || this.waiting() || !this.canStart()) return;
    const generation = this.generation;
    this.waiting.set(true);
    this.error.set(null);
    const key = crypto.randomUUID();
    let draft: WithdrawalDraft;
    try {
      draft = await this.drafts.prepare(scope, { operationKey: key, currentVersion: change.version,
        changeId: change.changeId, acceptedEffectiveAt: change.effectiveAt, confirmed: true });
    } catch {
      if (this.current(scope, generation)) {
        this.error.set('storage');
        this.waiting.set(false);
      }
      return;
    }
    if (!this.current(scope, generation)) return;
    this.draft.set(draft);
    this.reviewing.set(false);
    this.waiting.set(false);
    if (draft.command.operationKey === key && !draft.settled) await this.resolve(true);
  }

  protected async resolve(send: boolean) {
    const scope = this.scope();
    const draft = this.draft();
    if (!scope || !draft || draft.settled || this.waiting()) return;
    const generation = this.generation;
    const command = draft.command;
    this.waiting.set(true);
    this.error.set(null);
    try {
      const response = await (send ? this.api.withdraw(scope.account, scope.term, command)
        : this.api.recoverWithdrawal(scope.account, scope.term, command.operationKey));
      if (!this.matches(scope, command, response)) throw new Error('Withdrawal receipt does not match');
      await this.drafts.settle(scope, command.operationKey);
      if (!this.current(scope, generation)) return;
      this.restore();
      this.saved.emit();
    } catch (error) {
      await this.handleFailure(error, send, scope, generation, command.operationKey);
    } finally {
      if (this.current(scope, generation)) this.waiting.set(false);
    }
  }

  // A same-key send that is refused is definitive (the receipt lookup precedes every check); anything else is unknown.
  private async handleFailure(error: unknown, send: boolean, scope: CancellationScope, generation: number, key: string) {
    if (!this.current(scope, generation)) return;
    const failure = send && error instanceof HttpErrorResponse && error.status === 409
      ? definitive[error.error?.code as string] : undefined;
    if (!failure) {
      this.error.set('unknown');
      return;
    }
    try {
      await this.drafts.discardRejected(scope, key);
      if (!this.current(scope, generation)) return;
      this.checked.set(false);
      this.restore();
      this.error.set(failure);
      this.saved.emit();
    } catch {
      if (this.current(scope, generation)) this.error.set('storage');
    }
  }

  private matches(scope: CancellationScope, command: WithdrawalCommand, response: WithdrawalOutcome) {
    return response.accountId === scope.account && response.termId === scope.term
      && response.operationKey === command.operationKey && response.withdrawnChangeId === command.changeId
      && response.version === command.currentVersion + 1
      && Date.parse(response.effectiveAt) === Date.parse(command.acceptedEffectiveAt)
      && Date.parse(response.withdrawnAt) < Date.parse(response.effectiveAt);
  }

  protected instant(value: string) {
    return formatBillingInstant(value, this.localization.currentLanguage(), true);
  }

  private current(scope: CancellationScope, generation: number) {
    return this.scope() === scope && this.generation === generation;
  }
}
