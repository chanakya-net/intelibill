import { Injectable } from '@angular/core';
import type { CancellationScope } from './subscription-cancellation-drafts';
import type { WithdrawalCommand } from './subscription-cancellation.service';

export interface WithdrawalDraft {
  readonly command: WithdrawalCommand;
  readonly settled: boolean;
}

const isScheduledVersion = (version: unknown): version is number =>
  Number.isSafeInteger(version) && (version as number) >= 1 && (version as number) % 2 === 1;

/**
 * The withdrawal command frozen before HTTP, one per actor/account/term. An unsettled command is resent or
 * recovered with its key until a definitive answer; a settled one stays until a newer schedule supersedes it.
 */
@Injectable({ providedIn: 'root' })
export class SubscriptionCancellationWithdrawalDrafts {
  read(scope: CancellationScope): WithdrawalDraft | null {
    const raw = localStorage.getItem(this.key(scope));
    if (raw === null) return null;
    const value = JSON.parse(raw) as WithdrawalDraft | null;
    const command = value?.command;
    if (typeof value?.settled !== 'boolean' || !command?.operationKey || command.confirmed !== true
      || !isScheduledVersion(command.currentVersion) || !command.changeId || !command.acceptedEffectiveAt)
      throw new Error('Invalid withdrawal draft');
    return value;
  }

  prepare(scope: CancellationScope, command: WithdrawalCommand): Promise<WithdrawalDraft> {
    return navigator.locks.request(this.key(scope), () => {
      const prior = this.read(scope);
      if (prior && !(prior.settled && prior.command.currentVersion < command.currentVersion)) return prior;
      const draft = { command, settled: false };
      localStorage.setItem(this.key(scope), JSON.stringify(draft));
      return draft;
    });
  }

  settle(scope: CancellationScope, operationKey: string): Promise<void> {
    return navigator.locks.request(this.key(scope), () => {
      const draft = this.read(scope);
      if (draft?.command.operationKey === operationKey && !draft.settled)
        localStorage.setItem(this.key(scope), JSON.stringify({ ...draft, settled: true }));
    });
  }

  discardRejected(scope: CancellationScope, operationKey: string): Promise<void> {
    return navigator.locks.request(this.key(scope), () => {
      const draft = this.read(scope);
      if (draft?.command.operationKey === operationKey && !draft.settled)
        localStorage.removeItem(this.key(scope));
    });
  }

  private key(scope: CancellationScope): string {
    return 'intelibill.billing.cancellation-withdrawal.v1:' + JSON.stringify([scope.actor, scope.account, scope.term]);
  }
}
