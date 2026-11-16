import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonDirective } from 'primeng/button';
import { AuthService } from '../../../core/auth/auth.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { LocaleMoneyPipe } from '../../../shared/pipes/locale-money.pipe';
import { formatBillingInstant, type SubscriptionTermStatus } from '../status/subscription-status.models';
import { SubscriptionCancellationDrafts, type CancellationDraft, type CancellationScope } from './subscription-cancellation-drafts';
import { SubscriptionCancellationService, type CancellationDetail } from './subscription-cancellation.service';

@Component({
  selector: 'app-subscription-cancellation',
  imports: [ButtonDirective, TranslocoPipe, LocaleMoneyPipe],
  templateUrl: './subscription-cancellation.component.html',
  styles: [`
    :host { display: grid; gap: 0.75rem; }
    button { justify-self: start; }
    section { display: grid; gap: 0.5rem; }
    p, ul { margin: 0; }
    ul { list-style: disc; padding-inline-start: 1.5rem; }
    label { display: flex; gap: 0.5rem; align-items: baseline; }
    .actions { display: flex; flex-wrap: wrap; gap: 0.5rem; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:storage)': 'restore()' },
})
export class SubscriptionCancellationComponent {
  readonly account = input.required<string>();
  readonly term = input.required<SubscriptionTermStatus>();
  readonly saved = output<void>();
  private readonly api = inject(SubscriptionCancellationService);
  private readonly drafts = inject(SubscriptionCancellationDrafts);
  private readonly auth = inject(AuthService);
  private readonly localization = inject(LocalizationService);
  private readonly scope = computed(() => {
    const actor = this.auth.session()?.user.id;
    return actor ? { actor, account: this.account(), term: this.term().termId } : null;
  }, { equal: (left, right) => left?.actor === right?.actor && left?.account === right?.account && left?.term === right?.term });
  protected readonly detail = signal<CancellationDetail | null>(null);
  protected readonly draft = signal<CancellationDraft | null>(null);
  protected readonly waiting = signal(false);
  protected readonly checked = signal(false);
  protected readonly error = signal<'unavailable' | 'storage' | 'unknown' | 'conflict' | null>(null);
  private generation = 0;

  constructor() {
    effect(() => {
      this.scope();
      untracked(() => {
        this.generation++;
        this.detail.set(null);
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

  protected async review() {
    const scope = this.scope();
    if (!scope || this.waiting()) return;
    const generation = this.generation;
    this.waiting.set(true);
    this.error.set(null);
    this.detail.set(null);
    this.checked.set(false);
    try {
      const detail = await this.api.detail(scope.account, scope.term, () => this.current(scope, generation));
      if (this.current(scope, generation)) this.detail.set(detail);
    } catch {
      if (this.current(scope, generation)) this.error.set('unavailable');
    } finally {
      if (this.current(scope, generation)) this.waiting.set(false);
    }
  }

  protected async confirm() {
    const scope = this.scope();
    const detail = this.detail();
    if (!scope || !detail?.sourceHash || !this.checked() || this.waiting() || this.draft()) return;
    const generation = this.generation;
    this.waiting.set(true);
    let draft: CancellationDraft;
    let key: string;
    try {
      key = crypto.randomUUID();
      draft = await this.drafts.prepare(scope, {
        operationKey: key, expectedVersion: detail.version, sourceHash: detail.sourceHash,
        acceptedEffectiveAt: detail.effectiveAt, affectedShopSetHash: detail.affectedShopSetHash,
        affectedShopCount: detail.affectedShopCount, policyVersion: detail.policyVersion, confirmed: true,
      });
    } catch {
      if (this.current(scope, generation)) {
        this.error.set('storage');
        this.waiting.set(false);
      }
      return;
    }
    if (!this.current(scope, generation)) return;
    this.draft.set(draft);
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
      const response = await (send ? this.api.schedule(scope.account, scope.term, command)
        : this.api.recover(scope.account, scope.term, command.operationKey));
      if (response.accountId !== scope.account || response.termId !== scope.term || response.operationKey !== command.operationKey
        || response.cancellation.effectiveAt !== command.acceptedEffectiveAt || response.affectedShopCount !== command.affectedShopCount)
        throw new Error('Cancellation receipt does not match');
      await this.drafts.settle(scope, command.operationKey);
      if (this.current(scope, generation)) this.saved.emit();
    } catch (error) {
      await this.handleResolutionFailure(error, send, scope, generation, command.operationKey);
    } finally {
      if (this.current(scope, generation)) this.waiting.set(false);
    }
  }

  private async handleResolutionFailure(error: unknown, send: boolean, scope: CancellationScope,
    generation: number, key: string) {
    if (!this.current(scope, generation)) return;
    if (!send || !(error instanceof HttpErrorResponse) || error.status !== 409
      || error.error?.code !== 'Billing.CancellationConflict') {
      this.error.set('unknown');
      return;
    }
    try {
      await this.drafts.discardRejected(scope, key);
      if (!this.current(scope, generation)) return;
      this.detail.set(null);
      this.checked.set(false);
      this.restore();
      this.error.set('conflict');
    } catch {
      if (this.current(scope, generation)) this.error.set('storage');
    }
  }

  protected instant(value: string) {
    return formatBillingInstant(value, this.localization.currentLanguage(), true);
  }

  private current(scope: CancellationScope, generation: number) {
    return this.scope() === scope && this.generation === generation;
  }
}
