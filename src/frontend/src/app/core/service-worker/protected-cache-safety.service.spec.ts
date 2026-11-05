import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { ShopContextService } from '../context/shop-context.service';
import { AppReloadService } from '../services/app-reload.service';
import { CacheSafetyState, ProtectedCacheMigrationService } from './protected-cache-migration.service';
import { ProtectedCacheSafetyService } from './protected-cache-safety.service';

describe('ProtectedCacheSafetyService', () => {
  let workerChanged: (() => void) | null = null;
  const migration = {
    ensureSafeVersion: vi.fn<() => Promise<CacheSafetyState>>(),
    purgeLegacyCaches: vi.fn<() => Promise<boolean>>(),
    watchWorkerChanges: vi.fn((listener: () => void) => {
      workerChanged = listener;
      return () => (workerChanged = null);
    }),
  };
  const appReload = { restartAtAppRoot: vi.fn(), reloadPage: vi.fn() };

  function setup(): { service: ProtectedCacheSafetyService; context: ShopContextService } {
    TestBed.configureTestingModule({
      providers: [
        { provide: ProtectedCacheMigrationService, useValue: migration },
        { provide: AppReloadService, useValue: appReload },
      ],
    });
    return { service: TestBed.inject(ProtectedCacheSafetyService), context: TestBed.inject(ShopContextService) };
  }

  afterEach(() => {
    vi.clearAllMocks();
    migration.ensureSafeVersion.mockReset();
    migration.purgeLegacyCaches.mockReset();
    workerChanged = null;
    TestBed.resetTestingModule();
  });

  it('reports checking until the migration settles, then its result', async () => {
    let settle!: (state: CacheSafetyState) => void;
    migration.ensureSafeVersion.mockReturnValue(new Promise((resolve) => (settle = resolve)));
    const { service } = setup();

    service.start();
    expect(service.state()).toBe('checking');

    settle('reloadRequired');
    await vi.waitFor(() => expect(service.state()).toBe('reloadRequired'));
  });

  it('runs the migration once however often it is started', () => {
    migration.ensureSafeVersion.mockResolvedValue('safe');
    const { service } = setup();

    service.start();
    service.start();

    expect(migration.ensureSafeVersion).toHaveBeenCalledTimes(1);
  });

  it('checks again when another worker takes control, so a late hand-over clears the notice', async () => {
    migration.ensureSafeVersion.mockResolvedValueOnce('reloadRequired').mockResolvedValueOnce('safe');
    const { service } = setup();
    service.start();
    await vi.waitFor(() => expect(service.state()).toBe('reloadRequired'));

    workerChanged?.();

    await vi.waitFor(() => expect(service.state()).toBe('safe'));
    expect(migration.ensureSafeVersion).toHaveBeenCalledTimes(2);
  });

  it('never runs two checks at once: a change during a check runs one more after it', async () => {
    let finishFirst!: (state: CacheSafetyState) => void;
    migration.ensureSafeVersion
      .mockReturnValueOnce(new Promise((resolve) => (finishFirst = resolve)))
      .mockResolvedValue('safe');
    const { service } = setup();
    service.start();

    workerChanged?.();
    workerChanged?.();
    expect(migration.ensureSafeVersion).toHaveBeenCalledTimes(1);

    finishFirst('reloadRequired');
    await vi.waitFor(() => expect(service.state()).toBe('safe'));
    expect(migration.ensureSafeVersion).toHaveBeenCalledTimes(2);
  });

  it('treats a migration that throws as blocked', async () => {
    migration.ensureSafeVersion.mockRejectedValue(new Error('unexpected'));
    const { service } = setup();

    service.start();

    await vi.waitFor(() => expect(service.state()).toBe('blocked'));
  });

  it('purges on every change of owner', () => {
    migration.ensureSafeVersion.mockResolvedValue('safe');
    migration.purgeLegacyCaches.mockResolvedValue(true);
    const { service, context } = setup();
    context.initialize({ userId: 'user-1', shopId: 'shop-a' });
    service.start();

    context.transition(null, 'sign-out', () => undefined);
    context.transition({ userId: 'user-2', shopId: 'shop-b' }, 'sign-in', () => undefined);

    expect(migration.purgeLegacyCaches).toHaveBeenCalledTimes(2);
  });

  it('holds a shop switch restart until the purge finishes', async () => {
    migration.ensureSafeVersion.mockResolvedValue('safe');
    let finishPurge!: (purged: boolean) => void;
    migration.purgeLegacyCaches.mockReturnValue(new Promise((resolve) => (finishPurge = resolve)));
    const { service, context } = setup();
    context.initialize({ userId: 'user-1', shopId: 'shop-a' });
    service.start();

    context.transition({ userId: 'user-1', shopId: 'shop-b' }, 'scope', () => undefined);
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(appReload.restartAtAppRoot).not.toHaveBeenCalled();

    finishPurge(true);
    await vi.waitFor(() => expect(appReload.restartAtAppRoot).toHaveBeenCalledTimes(1));
  });

  it('shows blocked when a purge on sign-out fails, and clears it when a later one succeeds', async () => {
    migration.ensureSafeVersion.mockResolvedValue('safe');
    migration.purgeLegacyCaches.mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    const { service, context } = setup();
    context.initialize({ userId: 'user-1', shopId: 'shop-a' });
    service.start();
    await vi.waitFor(() => expect(service.state()).toBe('safe'));

    context.transition(null, 'sign-out', () => undefined);
    await vi.waitFor(() => expect(service.state()).toBe('blocked'));

    context.transition({ userId: 'user-2', shopId: 'shop-b' }, 'sign-in', () => undefined);
    await vi.waitFor(() => expect(service.state()).toBe('safe'));
  });

  it('keeps reloadRequired when a later purge fails: the reload is still what finishes it', async () => {
    migration.ensureSafeVersion.mockResolvedValue('reloadRequired');
    migration.purgeLegacyCaches.mockResolvedValue(false);
    const { service, context } = setup();
    context.initialize({ userId: 'user-1', shopId: 'shop-a' });
    service.start();
    await vi.waitFor(() => expect(service.state()).toBe('reloadRequired'));

    context.transition(null, 'sign-out', () => undefined);
    await Promise.resolve();

    expect(service.state()).toBe('reloadRequired');
  });

  it('reloads the page in place', () => {
    const { service } = setup();

    service.reload();

    expect(appReload.reloadPage).toHaveBeenCalledTimes(1);
    expect(appReload.restartAtAppRoot).not.toHaveBeenCalled();
  });
});
