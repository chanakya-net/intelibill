import { computed, signal } from '@angular/core';
import type { CheckoutScope } from '../checkout/checkout.models';
import type { RefundRequestItem, RefundRequestPage } from './refund-request.models';

export type RefundListLoader = (scope: CheckoutScope, after?: string) => Promise<RefundRequestPage>;
export type RefundListErrorMapper = (error: unknown) => string;

function mergeById(
  existing: readonly RefundRequestItem[],
  incoming: readonly RefundRequestItem[],
): readonly RefundRequestItem[] {
  const byId = new Map(existing.map(item => [item.refundRequestId, item]));
  for (const item of incoming) byId.set(item.refundRequestId, item);
  return Array.from(byId.values());
}

/**
 * Owns the merchant refund-request list state for the active billing scope.
 *
 * All server list reads (initial load, cross-tab settlement refresh, pagination) share one monotonic read epoch so
 * stale completions cannot overwrite a newer read's rows, cursor, error or loading state. Locally acknowledged POST
 * results are retained separately until the scope resets and deduplicate once a server page includes the same ID.
 */
export class RefundRequestListState {
  private readonly serverItems = signal<readonly RefundRequestItem[]>([]);
  private readonly acknowledged = signal<readonly RefundRequestItem[]>([]);
  private readonly cursorState = signal<string | null>(null);
  private readonly loadedState = signal(false);
  private readonly loadingState = signal(false);
  private readonly errorKeyState = signal<string | null>(null);
  private readEpoch = 0;

  readonly items = computed<readonly RefundRequestItem[]>(() => {
    const server = this.serverItems();
    const ack = this.acknowledged();
    if (ack.length === 0) return server;
    const listed = new Set(server.map(item => item.refundRequestId));
    const extra = ack.filter(item => !listed.has(item.refundRequestId));
    return extra.length === 0 ? server : [...server, ...extra];
  });
  readonly nextCursor = this.cursorState.asReadonly();
  readonly loaded = this.loadedState.asReadonly();
  readonly loading = this.loadingState.asReadonly();
  readonly errorKey = this.errorKeyState.asReadonly();

  constructor(
    private readonly loadPage: RefundListLoader,
    private readonly mapError: RefundListErrorMapper,
  ) {}

  reset(): void {
    this.readEpoch++;
    this.serverItems.set([]);
    this.acknowledged.set([]);
    this.cursorState.set(null);
    this.loadedState.set(false);
    this.loadingState.set(false);
    this.errorKeyState.set(null);
  }

  acknowledge(item: RefundRequestItem): void {
    this.acknowledged.update(existing => mergeById(existing, [item]));
  }

  refresh(scope: CheckoutScope, isScopeCurrent: () => boolean): Promise<void> {
    return this.runRead(scope, undefined, false, isScopeCurrent);
  }

  loadMore(scope: CheckoutScope, isScopeCurrent: () => boolean): Promise<void> {
    const cursor = this.cursorState();
    if (!cursor || this.loadingState()) return Promise.resolve();
    return this.runRead(scope, cursor, true, isScopeCurrent);
  }

  private async runRead(
    scope: CheckoutScope,
    after: string | undefined,
    append: boolean,
    isScopeCurrent: () => boolean,
  ): Promise<void> {
    if (!isScopeCurrent()) return;
    const read = ++this.readEpoch;
    const active = () => read === this.readEpoch && isScopeCurrent();
    this.loadingState.set(true);
    this.errorKeyState.set(null);
    try {
      const page = await this.loadPage(scope, after);
      if (!active()) return;
      this.serverItems.set(append ? mergeById(this.serverItems(), page.items) : mergeById([], page.items));
      this.cursorState.set(page.nextCursor);
      this.loadedState.set(true);
      this.errorKeyState.set(null);
    } catch (error) {
      if (active()) this.errorKeyState.set(this.mapError(error));
    } finally {
      if (active()) this.loadingState.set(false);
    }
  }
}
