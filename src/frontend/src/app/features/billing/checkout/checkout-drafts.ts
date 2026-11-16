import { Injectable } from '@angular/core';
import { isHash, isUuid, type AcceptQuote, type CheckoutScope, type CreateQuote } from './checkout.models';

export type CreateDraft = { readonly command: CreateQuote; readonly order: string | null };
export type AcceptDraft = { readonly command: AcceptQuote; readonly settled: boolean };
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

  private read<T>(key: string): T | null {
    const raw = localStorage.getItem(key);
    return raw === null ? null : JSON.parse(raw) as T;
  }

  private write<T>(key: string, value: T): T {
    localStorage.setItem(key, JSON.stringify(value));
    return value;
  }
}
