import 'fake-indexeddb/auto';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { FinancialOperationIntentService } from '../../../core/services/financial-operation-intent.service';
import { ExpiryPolicyService } from './expiry-policy.service';
import { BatchDispositionDraftService } from './batch-disposition-draft.service';

describe('durable batch disposition boundary', () => {
  it('keeps original root, version, reason and key after transport loss, reload and lookup404', async () => {
    const owner = { userId: crypto.randomUUID(), shopId: 'shop', epoch: 1 };
    let active = owner;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ShopContextService,
          useValue: {
            scope: () => active,
            epoch: () => active.epoch,
            isCurrent: (x: typeof owner) =>
              x.userId === active.userId && x.shopId === active.shopId,
          },
        },
        { provide: ExpiryPolicyService, useValue: { role: () => 'owner' } },
        { provide: FinancialOperationIntentService, useValue: {} },
      ],
    });
    const service = TestBed.inject(BatchDispositionDraftService);
    const http = TestBed.inject(HttpTestingController);
    const original = {
      expectedVersion: 8,
      quarantineChangeId: 'root-a',
      action: 'Dispose' as const,
      quantity: 0.25,
      reason: 'Damaged pack',
    };
    let request: ReturnType<HttpTestingController['expectOne']> | undefined;
    const first = firstValueFrom(service.execute('batch-a', original)).catch((x) => x);
    await vi.waitFor(() => {
      const found = http.match((x) => x.method === 'POST');
      expect(found).toHaveLength(1);
      request = found[0];
    });
    const frozen = structuredClone(request!.request.body);
    expect(frozen).toMatchObject(original);
    expect(frozen.operationKey).toBeTruthy();
    request!.error(new ProgressEvent('error'));
    await first;
    expect((await service.pending('batch-a'))[0].pending?.payload).toMatchObject(frozen);
    // A new adapter instance reads the same durable engine, without borrowing the edited form.
    const reloaded = TestBed.runInInjectionContext(() => new BatchDispositionDraftService());
    const second = TestBed.runInInjectionContext(() =>
      firstValueFrom(
        reloaded.execute('batch-a', {
          ...original,
          expectedVersion: 99,
          reason: 'Edited',
          quantity: 1,
        }),
      ).catch((x) => x),
    );
    await vi.waitFor(() => {
      const found = http.match((x) => x.method === 'GET');
      expect(found).toHaveLength(1);
      request = found[0];
    });
    expect(request!.request.url).toContain(
      `/financial-operations/batch-disposition/${frozen.operationKey}`,
    );
    request!.flush({}, { status: 404, statusText: 'Not found' });
    await vi.waitFor(() => {
      const found = http.match((x) => x.method === 'POST');
      expect(found).toHaveLength(1);
      request = found[0];
    });
    expect(request!.request.body).toEqual(frozen);
    request!.error(new ProgressEvent('error'));
    await second;
    active = { userId: owner.userId, shopId: 'other-shop', epoch: 2 };
    expect(await service.pending('batch-a')).toHaveLength(0);
    active = { ...owner, epoch: 3 };
    expect((await service.pending('batch-a'))[0].pending?.operationKey).toBe(frozen.operationKey);
    http.verify();
  });
});
