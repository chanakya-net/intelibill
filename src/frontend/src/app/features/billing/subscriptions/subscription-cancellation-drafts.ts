import { Injectable } from '@angular/core';
import type { CancellationCommand } from './subscription-cancellation.service';

export interface CancellationScope {
  readonly actor: string;
  readonly account: string;
  readonly term: string;
}

export interface CancellationDraft {
  readonly command: CancellationCommand;
  readonly settled: boolean;
}

/** One accepted operation per actor/account/term. A settled record is retained for other tabs. */
@Injectable({ providedIn: 'root' })
export class SubscriptionCancellationDrafts {
  read(scope: CancellationScope): CancellationDraft | null {
    const raw = localStorage.getItem(this.key(scope));
    if (raw === null) return null;
    const value = JSON.parse(raw) as CancellationDraft | null;
    const command = value?.command;
    if (typeof value?.settled !== 'boolean' || !command?.operationKey || command.confirmed !== true
      || command.expectedVersion !== 0 || !command.acceptedEffectiveAt || !Number.isSafeInteger(command.affectedShopCount)
      || command.affectedShopCount <= 0 || command.policyVersion !== 'term-boundary/1'
      || !/^[0-9a-f]{64}$/.test(command.sourceHash) || !/^[0-9a-f]{64}$/.test(command.affectedShopSetHash))
      throw new Error('Invalid cancellation draft');
    return value;
  }

  prepare(scope: CancellationScope, command: CancellationCommand): Promise<CancellationDraft> {
    return navigator.locks.request(this.key(scope), () => {
      const prior = this.read(scope);
      if (prior) return prior;
      const draft = { command, settled: false };
      this.write(scope, draft);
      return draft;
    });
  }

  settle(scope: CancellationScope, operationKey: string): Promise<void> {
    return navigator.locks.request(this.key(scope), () => {
      const draft = this.read(scope);
      if (draft?.command.operationKey === operationKey && !draft.settled)
        this.write(scope, { ...draft, settled: true });
    });
  }

  discardRejected(scope: CancellationScope, operationKey: string): Promise<void> {
    return navigator.locks.request(this.key(scope), () => {
      const draft = this.read(scope);
      if (draft?.command.operationKey === operationKey && !draft.settled)
        localStorage.removeItem(this.key(scope));
    });
  }

  private write(scope: CancellationScope, draft: CancellationDraft): void {
    localStorage.setItem(this.key(scope), JSON.stringify(draft));
  }

  private key(scope: CancellationScope): string {
    return 'intelibill.billing.cancellation.v1:' + JSON.stringify([scope.actor, scope.account, scope.term]);
  }
}
