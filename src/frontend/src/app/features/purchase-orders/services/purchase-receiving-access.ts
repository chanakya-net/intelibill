import { computed, effect, inject, signal } from '@angular/core';

import {
  OPERATING_ACCESS_ERROR_KEYS,
  OperatingAccessHintService,
} from '../../billing/status/operating-access-hint.service';
import { ReceivingDraftService } from './receiving-draft.service';
import type { PurchaseOrderStatus } from './purchase-order.service';

export class PurchaseReceivingAccess {
  readonly hint = inject(OperatingAccessHintService);
  readonly securityHidden = computed(
    () => this.hint.blockingMessage() === OPERATING_ACCESS_ERROR_KEYS['Licence.SecuritySuspended'],
  );
  private readonly drafts = inject(ReceivingDraftService);
  private readonly pendingIds = signal<ReadonlySet<string>>(new Set());
  private readonly refreshRevision = signal(0);

  constructor(orderIds: () => readonly string[]) {
    effect((onCleanup) => {
      this.refreshRevision();
      const scope = this.hint.scope();
      const ids = orderIds();
      this.pendingIds.set(new Set());
      if (!scope?.shopId || this.securityHidden() || !this.hint.blockingMessage()) return;
      let active = true;
      onCleanup(() => {
        active = false;
      });
      void Promise.all(
        ids.map(async (id) => {
          const record = await this.drafts.load(scope, id).catch(() => null);
          return record?.pending ? id : null;
        }),
      ).then((results) => {
        if (active && scope === this.hint.scope())
          this.pendingIds.set(new Set(results.filter((id) => id !== null)));
      });
    });
  }

  refreshPending(): void {
    this.refreshRevision.update((value) => value + 1);
  }

  label(id: string, status: PurchaseOrderStatus): string {
    return status === 'Received' || status === 'Closed' || this.pendingIds().has(id)
      ? 'purchaseOrders.receiving.recover'
      : 'purchaseOrders.actions.receive';
  }

  blocked(id: string): boolean {
    return !!this.hint.blockingMessage() && !this.pendingIds().has(id);
  }
}
