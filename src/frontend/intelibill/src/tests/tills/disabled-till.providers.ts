import { EMPTY } from 'rxjs';
import { signal } from '@angular/core';
import { TillContextService } from '../../app/features/tills/services/till-context.service';
import { TillDraftService } from '../../app/features/tills/services/till-draft.service';
import { TillService } from '../../app/features/tills/services/till.service';
/** Existing source suites explicitly run the rollout-disabled contract. Enabled tests use real services. */
export function disabledTillProviders() {
  return [
    {
      provide: TillContextService,
      useValue: {
        capability: signal({
          enabled: false,
          mixedTenderEnabled: false,
          businessDate: '2026-11-08',
        }),
        selected: signal(null),
        sessions: signal([]),
        owner: signal({ userId: 'user-1', shopId: 'shop-1' }),
        error: signal(false),
        enabled: signal(false),
        overrideReason: signal(''),
        posting: () => null,
        saleEvidence: () => ({}),
        offlineEvidence: async () => ({}),
        refresh: async () => undefined,
      },
    },
    { provide: TillDraftService, useValue: { list: async () => [], watch: () => EMPTY } },
    { provide: TillService, useValue: {} },
  ];
}
