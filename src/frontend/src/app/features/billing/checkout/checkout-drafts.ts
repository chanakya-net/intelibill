import { Injectable } from '@angular/core';
import { isHash, isUuid, type AcceptQuote, type CheckoutScope, type CreateQuote } from './checkout.models';

export type CreateDraft = { readonly command: CreateQuote; readonly order: string | null };
export type AcceptDraft = { readonly command: AcceptQuote; readonly settled: boolean };
export type StartPayment = { readonly operationKey: string; readonly expectedRevision: number; readonly snapshotHash: string };
export type PaymentDraftState = 'intent' | 'awaiting' | 'pending' | 'failed' | 'verified';
export type PaymentDraft = {
  readonly schemaVersion: 1;
  readonly userId: string;
  readonly account: string;
  readonly order: string;
  readonly command: StartPayment | null;
  readonly attempt: string | null;
  readonly state: PaymentDraftState;
};

const paymentStates: readonly PaymentDraftState[] = ['intent', 'awaiting', 'pending', 'failed', 'verified'];
const prefix = 'intelibill.billing.checkout.v1:';

@Injectable({ providedIn: 'root' })
export class CheckoutDrafts {
  key(scope: CheckoutScope, order?: string): string {
    return prefix + JSON.stringify([scope.userId, scope.account, order ?? null]);
  }

  readCreate(scope: CheckoutScope): CreateDraft | null {
    const value = this.read<CreateDraft>(this.key(scope));
    const command = value?.command;
    if (value && (!command || !isUuid(command.operationKey) || !isUuid(command.planVersionId) ||
      (command.period !== 'Month' && command.period !== 'Year') || command.expectedRevision !== 1 ||
      (value.order !== null && !isUuid(value.order)) || !Array.isArray(command.shopIds) ||
      command.shopIds.length > 100_000 || !command.shopIds.every(isUuid) ||
      !Number.isInteger(command.extraShops) || command.extraShops < 0 || command.extraShops > 100_000 ||
      (command.termId !== null && !isUuid(command.termId)))) throw new Error('Invalid quote draft');
    return value;
  }

  readAccept(scope: CheckoutScope, order: string): AcceptDraft | null {
    const value = this.read<AcceptDraft>(this.key(scope, order));
    const command = value?.command;
    if (value && (typeof value.settled !== 'boolean' || !command || !isUuid(command.operationKey) ||
      command.expectedRevision !== 1 || !isHash(command.snapshotHash) || !command.customerTermsVersion ||
      !command.refundPolicyVersion || command.confirmed !== true)) throw new Error('Invalid acceptance draft');
    return value;
  }

  async prepareCreate(scope: CheckoutScope, command: CreateQuote): Promise<CreateDraft> {
    const key = this.key(scope);
    return navigator.locks.request(key, () => this.readCreate(scope) ?? this.write(key, { command, order: null }));
  }

  async prepareAccept(scope: CheckoutScope, order: string, command: AcceptQuote): Promise<AcceptDraft> {
    const key = this.key(scope, order);
    return navigator.locks.request(key, () => this.readAccept(scope, order) ?? this.write(key, { command, settled: false }));
  }

  async finishCreate(scope: CheckoutScope, key: string, order: string | null): Promise<void> {
    const storageKey = this.key(scope);
    await navigator.locks.request(storageKey, () => {
      const draft = this.readCreate(scope);
      if (draft?.command.operationKey !== key || draft.order) return;
      if (order) this.write(storageKey, { ...draft, order });
      else localStorage.removeItem(storageKey);
    });
  }

  async startOver(scope: CheckoutScope, order: string): Promise<void> {
    const key = this.key(scope);
    await navigator.locks.request(key, () => {
      if (this.readCreate(scope)?.order === order) localStorage.removeItem(key);
    });
  }

  async finishAccept(scope: CheckoutScope, order: string, key: string, accepted: boolean): Promise<void> {
    const storageKey = this.key(scope, order);
    await navigator.locks.request(storageKey, () => {
      const draft = this.readAccept(scope, order);
      if (draft?.command.operationKey !== key || draft.settled) return;
      if (accepted) this.write(storageKey, { ...draft, settled: true });
      else localStorage.removeItem(storageKey);
    });
  }

  paymentKey(scope: CheckoutScope, order: string): string {
    return prefix + 'payment:' + JSON.stringify([scope.userId, scope.account, order]);
  }

  readPayment(scope: CheckoutScope, order: string): PaymentDraft | null {
    const key = this.paymentKey(scope, order);
    const value = this.read<PaymentDraft>(key);
    const command = value?.command;
    if (value === null && localStorage.getItem(key) === null) return null;
    if (!value || value.schemaVersion !== 1 || value.userId !== scope.userId || value.account !== scope.account ||
      value.order !== order || !paymentStates.includes(value.state) ||
      (value.state === 'intent' ? value.attempt !== null || !command : !isUuid(value.attempt)) ||
      (command !== null && (!command || !isUuid(command.operationKey) || !isHash(command.snapshotHash) ||
      !Number.isSafeInteger(command.expectedRevision) || command.expectedRevision < 1))) throw new Error('Invalid payment draft');
    return value;
  }

  preparePayment(scope: CheckoutScope, order: string, revision: number, hash: string): Promise<PaymentDraft> {
    const key = this.paymentKey(scope, order);
    return navigator.locks.request(key, () => this.readPayment(scope, order) ?? this.write(key, {
      schemaVersion: 1, userId: scope.userId, account: scope.account, order, state: 'intent', attempt: null,
      command: { operationKey: crypto.randomUUID(), expectedRevision: revision, snapshotHash: hash },
    }));
  }

  recordPayment(scope: CheckoutScope, order: string, attempt: string, state: Exclude<PaymentDraftState, 'intent'>) {
    const key = this.paymentKey(scope, order);
    return navigator.locks.request(key, () => {
      const saved = this.readPayment(scope, order);
      if (saved?.attempt && saved.attempt !== attempt) throw new Error('Checkout attempt changed');
      // The server reports a provider-confirmed unpaid order as awaiting again; that is the one state that may reopen.
      const reopens = saved?.state === 'pending' && state === 'awaiting';
      if (saved && !reopens && paymentStates.indexOf(saved.state) >= paymentStates.indexOf(state)) return saved;
      return this.write<PaymentDraft>(key, {
        schemaVersion: 1, userId: scope.userId, account: scope.account, order, attempt, state,
        command: saved?.command ?? null,
      });
    });
  }

  private read<T>(key: string): T | null {
    const raw = localStorage.getItem(key);
    return raw === null ? null : JSON.parse(raw) as T;
  }

  private write<T>(key: string, value: T): T {
    localStorage.setItem(key, JSON.stringify(value));
    return value;
  }
}
