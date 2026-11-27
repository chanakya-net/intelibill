import { computed, inject } from '@angular/core';

import {
  OPERATING_ACCESS_ERROR_KEYS,
  OperatingAccessHintService,
} from '../../billing/status/operating-access-hint.service';
import type { PurchaseOrderStatus } from './purchase-order.service';

export class PurchaseReceivingAccess {
  readonly hint = inject(OperatingAccessHintService);
  readonly securityHidden = computed(
    () => this.hint.blockingMessage() === OPERATING_ACCESS_ERROR_KEYS['Licence.SecuritySuspended'],
  );

  label(status: PurchaseOrderStatus): string {
    return this.hint.blockingMessage() || status === 'Received' || status === 'Closed'
      ? 'purchaseOrders.receiving.recover'
      : 'purchaseOrders.actions.receive';
  }
}
