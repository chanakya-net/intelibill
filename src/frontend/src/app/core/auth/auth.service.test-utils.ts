import '@angular/compiler';

import { PLATFORM_ID } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { afterAll, afterEach, beforeAll, beforeEach, vi } from 'vitest';

import { AuthResult, AuthSession } from './auth.models';
import { AuthService } from './auth.service';
import { AuthStorage } from './auth.storage';
import { LocalizationService } from '../i18n/localization.service';
import { NetworkStatusService } from '../services/network-status.service';
import { OfflineSalesDeviceSettingsStorage } from '../storage/offline-sales-device-settings.storage';
import { OfflineSalesSnapshotIndexedDbService } from '../storage/offline-sales-snapshot-indexeddb.service';

export class FakeBroadcastChannel {
  static instances: FakeBroadcastChannel[] = [];

  static reset(): void {
    FakeBroadcastChannel.instances = [];
  }

  readonly name: string;
  onmessage: ((event: MessageEvent<unknown>) => void) | null = null;

  private listeners = new Set<(event: MessageEvent<unknown>) => void>();
  private closed = false;

  constructor(name: string) {
    this.name = name;
    FakeBroadcastChannel.instances.push(this);
  }

  addEventListener(type: string, listener: EventListenerOrEventListenerObject): void {
    if (type !== 'message') {
      return;
    }

    if (typeof listener === 'function') {
      this.listeners.add(listener as (event: MessageEvent<unknown>) => void);
      return;
    }

    this.listeners.add((event) => listener.handleEvent(event));
  }

  removeEventListener(type: string, listener: EventListenerOrEventListenerObject): void {
    if (type !== 'message' || typeof listener !== 'function') {
      return;
    }

    this.listeners.delete(listener as (event: MessageEvent<unknown>) => void);
  }

  postMessage(message: unknown): void {
    for (const instance of FakeBroadcastChannel.instances) {
      if (instance === this || instance.closed || instance.name !== this.name) {
        continue;
      }

      instance.dispatch(message);
    }
  }

  close(): void {
    this.closed = true;
  }

  private dispatch(data: unknown): void {
    const event = { data } as MessageEvent<unknown>;
    this.onmessage?.(event);
    for (const listener of this.listeners) {
      listener(event);
    }
  }
}

export function buildAuthResult(overrides?: Partial<AuthResult>): AuthResult {
  const now = Date.now();

  return {
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessTokenExpiresAt: new Date(now + 60_000).toISOString(),
    refreshTokenExpiresAt: new Date(now + 120_000).toISOString(),
    user: {
      id: 'user-1',
      email: 'user@example.com',
      phoneNumber: null,
      firstName: 'Test',
      lastName: 'User',
    },
    activeShopId: null,
    shops: [],
    ...overrides,
  };
}

export function buildSession(overrides?: Partial<AuthSession>): AuthSession {
  const result = buildAuthResult();

  return {
    accessToken: result.accessToken,
    refreshToken: result.refreshToken,
    accessTokenExpiresAt: result.accessTokenExpiresAt,
    refreshTokenExpiresAt: result.refreshTokenExpiresAt,
    rememberMe: true,
    user: result.user,
    activeShopId: result.activeShopId,
    shops: result.shops,
    ...overrides,
  };
}

export function createAuthServiceTestHarness(): {
  setup: () => { service: AuthService; http: HttpTestingController };
  storage: {
    loadSession: ReturnType<typeof vi.fn<AuthStorage['loadSession']>>;
    saveSession: ReturnType<typeof vi.fn<AuthStorage['saveSession']>>;
    clearSession: ReturnType<typeof vi.fn<AuthStorage['clearSession']>>;
    saveLastIdentifier: ReturnType<typeof vi.fn<AuthStorage['saveLastIdentifier']>>;
    getLastIdentifier: ReturnType<typeof vi.fn<AuthStorage['getLastIdentifier']>>;
    clearLastIdentifier: ReturnType<typeof vi.fn<AuthStorage['clearLastIdentifier']>>;
  };
  router: { navigateByUrl: ReturnType<typeof vi.fn<Router['navigateByUrl']>> };
  localizationService: { setLanguage: ReturnType<typeof vi.fn<LocalizationService['setLanguage']>> };
  networkStatus: {
    canReachApi: ReturnType<typeof vi.fn<NetworkStatusService['canReachApi']>>;
    checkConnectivity: ReturnType<typeof vi.fn<NetworkStatusService['checkConnectivity']>>;
  };
  offlineSalesDeviceSettingsStorage: { loadSettings: ReturnType<typeof vi.fn<OfflineSalesDeviceSettingsStorage['loadSettings']>> };
  offlineSalesSnapshotDb: { getUsableSnapshotInfo: ReturnType<typeof vi.fn<OfflineSalesSnapshotIndexedDbService['getUsableSnapshotInfo']>> };
} {
  const originalBroadcastChannel = globalThis.BroadcastChannel;
  const storage = {
    loadSession: vi.fn<AuthStorage['loadSession']>(),
    saveSession: vi.fn<AuthStorage['saveSession']>(),
    clearSession: vi.fn<AuthStorage['clearSession']>(),
    saveLastIdentifier: vi.fn<AuthStorage['saveLastIdentifier']>(),
    getLastIdentifier: vi.fn<AuthStorage['getLastIdentifier']>(),
    clearLastIdentifier: vi.fn<AuthStorage['clearLastIdentifier']>(),
  };

  const router = {
    navigateByUrl: vi.fn<Router['navigateByUrl']>(),
  };

  const localizationService = {
    setLanguage: vi.fn<LocalizationService['setLanguage']>().mockResolvedValue(undefined),
  };
  const networkStatus = {
    canReachApi: vi.fn<NetworkStatusService['canReachApi']>(),
    checkConnectivity: vi.fn<NetworkStatusService['checkConnectivity']>().mockResolvedValue(undefined),
  };
  const offlineSalesDeviceSettingsStorage = {
    loadSettings: vi.fn<OfflineSalesDeviceSettingsStorage['loadSettings']>(),
  };
  const offlineSalesSnapshotDb = {
    getUsableSnapshotInfo: vi.fn<OfflineSalesSnapshotIndexedDbService['getUsableSnapshotInfo']>(),
  };

  function setup(): { service: AuthService; http: HttpTestingController } {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PLATFORM_ID, useValue: 'browser' },
        { provide: AuthStorage, useValue: storage },
        { provide: Router, useValue: router },
        { provide: LocalizationService, useValue: localizationService },
        { provide: NetworkStatusService, useValue: networkStatus },
        { provide: OfflineSalesDeviceSettingsStorage, useValue: offlineSalesDeviceSettingsStorage },
        { provide: OfflineSalesSnapshotIndexedDbService, useValue: offlineSalesSnapshotDb },
      ],
    });

    return {
      service: TestBed.inject(AuthService),
      http: TestBed.inject(HttpTestingController),
    };
  }

  beforeAll(() => {
    vi.stubGlobal('BroadcastChannel', FakeBroadcastChannel);
  });

  beforeEach(() => {
    FakeBroadcastChannel.reset();
    storage.loadSession.mockReturnValue(null);
    storage.saveSession.mockReset();
    storage.clearSession.mockReset();
    storage.saveLastIdentifier.mockReset();
    storage.getLastIdentifier.mockReturnValue('');
    storage.clearLastIdentifier.mockReset();
    router.navigateByUrl.mockResolvedValue(true);
    localizationService.setLanguage.mockClear();
    networkStatus.canReachApi.mockReturnValue(true);
    networkStatus.checkConnectivity.mockClear();
    offlineSalesDeviceSettingsStorage.loadSettings.mockReset();
    offlineSalesSnapshotDb.getUsableSnapshotInfo.mockReset();
  });

  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  afterAll(() => {
    if (originalBroadcastChannel) {
      vi.stubGlobal('BroadcastChannel', originalBroadcastChannel);
      return;
    }

    vi.unstubAllGlobals();
  });

  return {
    setup,
    storage,
    router,
    localizationService,
    networkStatus,
    offlineSalesDeviceSettingsStorage,
    offlineSalesSnapshotDb,
  };
}
