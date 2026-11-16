import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { CheckoutDrafts, type AcceptDraft, type CreateDraft } from './checkout-drafts';
import { isUuid, shopSetHash, type CheckoutScope, type CheckoutShop, type CreateQuote, type Quote } from './checkout.models';
import { CheckoutService } from './checkout.service';

export type Failure = 'unavailable' | 'storage' | 'unknown' | 'invalid' | 'changed' | 'expired' | 'denied';
export type Invocation = { readonly scope: CheckoutScope; readonly current: () => boolean };

@Injectable()
export class CheckoutReview {
  private readonly api = inject(CheckoutService);
  private readonly drafts = inject(CheckoutDrafts);

  readonly quote = signal<Quote | null>(null);
  readonly covered = signal<readonly CheckoutShop[] | null>(null);
  readonly createDraft = signal<CreateDraft | null>(null);
  readonly acceptDraft = signal<AcceptDraft | null>(null);
  readonly problem = signal<Failure | null>(null);

  reset() {
    this.quote.set(null);
    this.covered.set(null);
    this.createDraft.set(null);
    this.acceptDraft.set(null);
  }

  async restore(invocation: Invocation, order: string | null | undefined): Promise<boolean> {
    const draft = this.readLocal(() => this.drafts.readCreate(invocation.scope));
    if (!order) this.createDraft.set(draft);
    const retained = order ?? draft?.order;
    if (retained && isUuid(retained)) await this.restoreOrder(invocation, retained);
    else if (order) this.problem.set('invalid');
    else if (draft) await this.recoverCreate(invocation, draft);
    else return false;
    return true;
  }

  async create(invocation: Invocation, selection: Omit<CreateQuote, 'operationKey'>) {
    const key = crypto.randomUUID();
    const draft = await this.drafts.prepareCreate(invocation.scope, { operationKey: key, ...selection });
    if (!invocation.current()) return;
    this.createDraft.set(draft);
    if (draft.command.operationKey === key) await this.sendCreate(invocation, draft);
    else if (draft.order) await this.restoreOrder(invocation, draft.order);
    else this.problem.set('unknown');
  }

  async accept(invocation: Invocation, quote: Quote) {
    const draft = await this.drafts.prepareAccept(invocation.scope, quote.orderId, {
      operationKey: crypto.randomUUID(), expectedRevision: quote.revision, snapshotHash: quote.snapshotHash,
      customerTermsVersion: quote.customerTermsVersion, refundPolicyVersion: quote.refundPolicyVersion, confirmed: true,
    });
    if (!invocation.current()) return;
    this.acceptDraft.set(draft);
    await this.sendAccept(invocation, quote, draft);
  }

  async retry(invocation: Invocation) {
    const create = this.createDraft();
    const accept = this.acceptDraft();
    const quote = this.quote();
    if (accept && quote) await this.sendAccept(invocation, quote, accept);
    else if (create && !create.order) await this.sendCreate(invocation, create);
  }

  private async recoverCreate(invocation: Invocation, draft: CreateDraft) {
    try {
      await this.saveCreated(invocation, draft,
        await this.api.recover(invocation.scope, 'create', draft.command.operationKey));
    } catch (error) {
      if (error instanceof DraftError) throw error;
      if (invocation.current()) this.problem.set(checkoutFailure(error) ?? 'unknown');
    }
  }

  private async sendCreate(invocation: Invocation, draft: CreateDraft) {
    const { scope, current } = invocation;
    try {
      await this.saveCreated(invocation, draft, await this.api.create(scope, draft.command));
    } catch (error) {
      if (error instanceof DraftError) throw error;
      const failure = checkoutFailure(error);
      if (failure === 'invalid' || failure === 'changed') {
        await this.writeLocal(() => this.drafts.finishCreate(scope, draft.command.operationKey, null));
        if (current()) this.createDraft.set(null);
      }
      if (current()) this.problem.set(failure ?? 'unknown');
    }
  }

  private async saveCreated(invocation: Invocation, draft: CreateDraft, quote: Quote) {
    const { scope, current } = invocation;
    const command = draft.command;
    if (quote.accountId !== scope.account || quote.planVersionId !== command.planVersionId ||
      quote.period !== command.period || quote.termId !== command.termId ||
      (!quote.termId && (quote.shopCount !== command.shopIds.length ||
        await shopSetHash(command.shopIds) !== quote.shopHash))) throw new Error('Quote does not match selection');
    await this.writeLocal(() => this.drafts.finishCreate(scope, command.operationKey, quote.orderId));
    if (current()) {
      this.createDraft.set({ ...draft, order: quote.orderId });
      await this.show(invocation, quote);
    }
  }

  private async restoreOrder(invocation: Invocation, order: string) {
    const { scope, current } = invocation;
    const draft = this.readLocal(() => this.drafts.readAccept(scope, order));
    this.acceptDraft.set(draft);
    if (draft && !draft.settled) {
      try {
        const recovered = await this.api.recover(scope, 'accept', draft.command.operationKey);
        await this.saveAccepted(invocation, order, draft, recovered);
        return;
      } catch (error) {
        if (error instanceof DraftError) throw error;
        if (current()) this.problem.set(checkoutFailure(error) ?? 'unknown');
      }
    }
    if (!current()) return;
    const quote = await this.api.read(scope, order);
    if (draft && !draft.settled && quote.acceptance) {
      if (quote.acceptance.operationKey === draft.command.operationKey) {
        await this.saveAccepted(invocation, order, draft, quote);
        return;
      }
      await this.writeLocal(() => this.drafts.finishAccept(scope, order, draft.command.operationKey, false));
      if (current()) { this.acceptDraft.set(null); this.problem.set(null); }
    }
    await this.show(invocation, quote);
  }

  private async sendAccept(invocation: Invocation, quote: Quote, draft: AcceptDraft) {
    const { scope, current } = invocation;
    try {
      await this.saveAccepted(invocation, quote.orderId, draft, await this.api.accept(scope, quote.orderId, draft.command));
    } catch (error) {
      if (error instanceof DraftError) throw error;
      if (error instanceof HttpErrorResponse && error.status === 409 &&
        error.error?.code === 'Billing.CheckoutAlreadyAccepted') {
        if (current()) await this.restoreOrder(invocation, quote.orderId);
        return;
      }
      const failure = checkoutFailure(error);
      if (failure === 'expired' || failure === 'changed' || failure === 'invalid') {
        await this.writeLocal(() => this.drafts.finishAccept(scope, quote.orderId, draft.command.operationKey, false));
        if (current()) this.acceptDraft.set(null);
      }
      if (current()) this.problem.set(failure ?? 'unknown');
    }
  }

  private async saveAccepted(invocation: Invocation, order: string, draft: AcceptDraft, quote: Quote) {
    const { scope, current } = invocation;
    const { operationKey, snapshotHash } = draft.command;
    if (quote.accountId !== scope.account || quote.orderId !== order || quote.acceptance?.operationKey !== operationKey ||
      quote.snapshotHash !== snapshotHash || quote.acceptance.snapshotHash !== snapshotHash)
      throw new Error('Acceptance does not match review');
    await this.writeLocal(() => this.drafts.finishAccept(scope, order, operationKey, true));
    if (!current()) return;
    this.acceptDraft.set({ ...draft, settled: true });
    this.problem.set(null);
    await this.show(invocation, quote);
  }

  private async show({ scope, current }: Invocation, quote: Quote) {
    if (!current()) return;
    if (quote.accountId !== scope.account || quote.schemaVersion !== 1) throw new Error('Invalid quote');
    this.quote.set(quote);
    this.covered.set(null);
    try {
      const shops = await this.api.shops(scope, quote, current);
      if (current()) this.covered.set(shops);
    } catch {
      if (current()) this.problem.set('unavailable');
    }
  }

  private readLocal<T>(operation: () => T): T {
    try { return operation(); } catch { throw new DraftError(); }
  }

  private async writeLocal<T>(operation: () => Promise<T>): Promise<T> {
    try { return await operation(); } catch { throw new DraftError(); }
  }
}

export class DraftError extends Error {}

export function checkoutFailure(error: unknown): Failure | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  if (error.status === 401 || error.status === 403) return 'denied';
  switch (error.error?.code) {
    case 'Billing.CheckoutExpired': return 'expired';
    case 'Billing.CheckoutSourceChanged': return 'changed';
    case 'Billing.CheckoutInvalid': return 'invalid';
    default: return null;
  }
}
