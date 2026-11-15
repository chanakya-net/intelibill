import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { problemCode } from '@intelibill/api';

const INITIAL = { revision: 0, denied: false } as const;

@Injectable({ providedIn: 'root' })
export class BillingAccountAuthority {
  private readonly states = signal(new Map<string, { revision: number; denied: boolean }>());

  state(user: string, account: string) {
    return this.states().get(JSON.stringify([user, account])) ?? INITIAL;
  }

  deny(user: string, account: string, failure: unknown): boolean {
    if (!(failure instanceof HttpErrorResponse) || failure.status !== 403 ||
        problemCode(failure.error) !== 'Billing.AccountAccessDenied') return false;
    this.set(user, account, this.state(user, account).revision + 1, true);
    return true;
  }

  confirm(user: string, account: string): void {
    const { revision, denied } = this.state(user, account);
    if (denied) this.set(user, account, revision, false);
  }

  private set(user: string, account: string, revision: number, denied: boolean): void {
    this.states.update(states => new Map(states).set(JSON.stringify([user, account]), { revision, denied }));
  }
}
