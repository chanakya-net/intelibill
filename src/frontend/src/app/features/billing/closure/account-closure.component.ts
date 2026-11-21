import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonDirective } from 'primeng/button';
import { AuthService } from '../../../core/auth/auth.service';
import { ClosureApi, type AccountClosureCommand, type AccountClosureProposal } from './closure-api.service';
import { ClosureDrafts, type OwnerBoundClosureDraft } from './closure-drafts';

interface AccountIntent {
  readonly proposalId: string;
  readonly shopIds: readonly string[];
  readonly command: AccountClosureCommand;
}

@Component({
  selector: 'app-account-closure',
  imports: [TranslocoPipe, ButtonDirective],
  templateUrl: './account-closure.component.html',
  styleUrl: './closure-confirmation.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:storage)': 'restore()' },
})
export class AccountClosureComponent {
  readonly account = input.required<string>();
  private readonly api = inject(ClosureApi);
  private readonly auth = inject(AuthService);
  private readonly drafts = inject(ClosureDrafts);
  private readonly scope = computed(() => {
    const session = this.auth.session();
    return session ? JSON.stringify([session.user.id, session.activeShopId, this.account(), 'billing-owner']) : null;
  });
  protected readonly proposal = signal<AccountClosureProposal | null>(null);
  protected readonly draft = signal<OwnerBoundClosureDraft<AccountIntent> | null>(null);
  protected readonly proposalDraft = signal<OwnerBoundClosureDraft<string> | null>(null);
  protected readonly busy = signal(false);
  protected readonly ready = signal(false);
  protected readonly confirmed = signal(false);
  protected readonly ownersReady = computed(() => this.proposal()?.shops.every(shop => shop.consentRecorded) === true);
  protected readonly message = signal<string | null>(null);
  private generation = 0;

  constructor() {
    effect(() => {
      this.scope();
      untracked(() => {
        this.generation++;
        this.proposal.set(null);
        this.ready.set(false);
        this.confirmed.set(false);
        this.message.set(null);
        this.busy.set(false);
        this.restore();
      });
    });
    inject(DestroyRef).onDestroy(() => this.generation++);
  }

  protected restore() {
    const scope = this.scope();
    try {
      this.draft.set(scope ? this.drafts.read<AccountIntent>(scope) : null);
      this.proposalDraft.set(scope ? this.drafts.read<string>(scope + '/proposal') : null);
    } catch {
      this.message.set('storage');
    }
  }

  protected async read() {
    const scope = this.scope();
    if (!scope || this.busy()) return;
    const generation = this.generation;
    this.begin();
    try {
      const saved = await this.api.accountProposal(this.account());
      if (this.current(scope, generation)) this.acceptProposal(saved);
    } catch (error) {
      if (!this.current(scope, generation)) return;
      this.proposal.set(null);
      if (error instanceof HttpErrorResponse && error.status === 404) this.ready.set(true);
      else this.message.set(error instanceof HttpErrorResponse && error.status === 403 ? 'billingOwner' : 'unavailable');
    } finally {
      if (this.current(scope, generation)) this.busy.set(false);
    }
  }

  protected async start() {
    const scope = this.scope();
    if (!scope || this.busy() || this.draft()) return;
    const account = this.account();
    const generation = this.generation;
    this.begin();
    try {
      const key = crypto.randomUUID();
      const draft = await this.drafts.prepare(scope + '/proposal', key, account, previous => previous.settled);
      if (!this.current(scope, generation)) return;
      this.proposalDraft.set(draft);
      if (draft.input !== account) throw new Error('Unexpected closure proposal account');
      const saved = await this.api.propose(account, draft.operationKey);
      if (saved.accountId !== account || saved.id !== draft.operationKey) throw new Error('Unexpected closure proposal');
      await this.drafts.finish(scope + '/proposal', draft.operationKey, true);
      if (this.current(scope, generation)) {
        this.acceptProposal(saved);
        this.restore();
      }
    } catch (error) {
      if (this.current(scope, generation)) this.message.set(
        error instanceof HttpErrorResponse && error.error?.code === 'Closure.ScopeTooLarge' ? 'scopeTooLarge' : 'proposalUnknown');
    } finally {
      if (this.current(scope, generation)) this.busy.set(false);
    }
  }

  protected async confirm() {
    const scope = this.scope();
    const proposal = this.proposal();
    if (!scope || !proposal || this.busy() || !this.confirmed() || this.draft() ||
      proposal.shops.some(shop => !shop.consentRecorded)) return;
    const generation = this.generation;
    const operationKey = crypto.randomUUID();
    this.busy.set(true);
    try {
      const draft = await this.drafts.prepare(scope, operationKey, {
        proposalId: proposal.id, shopIds: proposal.shops.map(shop => shop.shopId), command: { operationKey, scopeDigest: proposal.scopeDigest, confirmed: true } satisfies AccountClosureCommand,
      });
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
    const account = this.account();
    const generation = this.generation;
    this.begin();
    try {
      const saved = await this.api.confirm(account, draft.input.proposalId, draft.input.command);
      if (saved.accountId !== account || saved.scope !== 'Account' || saved.operationKey !== draft.operationKey
        || saved.financialTreatment !== 'NoFinancialChange' || saved.shops.length !== draft.input.shopIds.length
        || saved.shops.some((shop, index) => shop.shopId !== draft.input.shopIds[index]))
        throw new Error('Unexpected account closure');
      await this.drafts.finish(scope, draft.operationKey, true);
      if (this.current(scope, generation)) this.restore();
    } catch (error) {
      if (this.current(scope, generation)) await this.handleFailure(error, scope, generation, draft.operationKey);
    } finally {
      if (this.current(scope, generation)) this.busy.set(false);
    }
  }

  private async handleFailure(error: unknown, scope: string, generation: number, key: string) {
    if (!(error instanceof HttpErrorResponse) || error.status !== 409 || error.error?.code !== 'Closure.ReviewChanged') {
      this.message.set('unknown');
      return;
    }
    try {
      await this.drafts.finish(scope, key, false);
      if (!this.current(scope, generation)) return;
      this.restore();
      this.message.set('stale');
    } catch {
      if (this.current(scope, generation)) this.message.set('storage');
    }
  }

  private begin() {
    this.busy.set(true);
    this.message.set(null);
    this.confirmed.set(false);
  }

  private acceptProposal(proposal: AccountClosureProposal) {
    if (proposal.accountId === this.account() && proposal.shops.length) {
      this.proposal.set(proposal);
      this.ready.set(true);
    } else {
      this.message.set('unavailable');
    }
  }

  private current(scope: string, generation: number) {
    return generation === this.generation && this.scope() === scope;
  }
}
