import { OfflineSalesDeviceSettings, OfflineSalesDeviceSettingsStorage } from './offline-sales-device-settings.storage';

describe('OfflineSalesDeviceSettingsStorage', () => {
  let storage: OfflineSalesDeviceSettingsStorage;

  let originalLocalStorage: Storage | undefined;
  let originalCrypto: Crypto | undefined;
  let randomUuidCalls = 0;

  beforeEach(() => {
    storage = new OfflineSalesDeviceSettingsStorage();

    originalLocalStorage = (globalThis as any).localStorage as Storage | undefined;
    const store = new Map<string, string>();
    const mockLocalStorage: Storage = {
      get length() {
        return store.size;
      },
      clear: () => store.clear(),
      getItem: (key: string) => store.get(key) ?? null,
      key: (index: number) => Array.from(store.keys())[index] ?? null,
      removeItem: (key: string) => {
        store.delete(key);
      },
      setItem: (key: string, value: string) => {
        store.set(key, value);
      },
    } as unknown as Storage;

    Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: mockLocalStorage });

    originalCrypto = globalThis.crypto;
    randomUuidCalls = 0;

    Object.defineProperty(globalThis, 'crypto', {
      configurable: true,
      value: {
        randomUUID: () => {
          randomUuidCalls += 1;
          return `uuid-${randomUuidCalls}`;
        },
      },
    });
  });

  afterEach(() => {
    if (originalLocalStorage) {
      Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: originalLocalStorage });
    } else {
      // eslint-disable-next-line @typescript-eslint/no-dynamic-delete
      delete (globalThis as any).localStorage;
    }

    if (originalCrypto) {
      Object.defineProperty(globalThis, 'crypto', { configurable: true, value: originalCrypto });
    } else {
      // eslint-disable-next-line @typescript-eslint/no-dynamic-delete
      delete (globalThis as any).crypto;
    }
  });

  it('returns a stable device id per shop across repeated calls', () => {
    const first = storage.getOrCreateDeviceId('shop-1');
    const second = storage.getOrCreateDeviceId('shop-1');

    expect(first).toBeTruthy();
    expect(second).toBe(first);
  });

  it('creates different device ids for different shops', () => {
    const shop1 = storage.getOrCreateDeviceId('shop-1');
    const shop2 = storage.getOrCreateDeviceId('shop-2');

    expect(shop1).toBeTruthy();
    expect(shop2).toBeTruthy();
    expect(shop2).not.toBe(shop1);
  });

  it('round-trips settings via saveSettings/loadSettings', () => {
    const shopId = 'shop-1';
    const deviceId = storage.getOrCreateDeviceId(shopId);

    const settings: OfflineSalesDeviceSettings = {
      shopId,
      deviceId,
      label: 'Front counter',
      enabled: true,
      enabledAt: '2026-05-21T12:00:00.000Z',
      enabledByUserId: 'user-1',
      enabledByUserName: 'Owner',
      lastCompleteSnapshotAt: '2026-05-21T12:01:00.000Z',
      lastApiVerifiedAt: '2026-05-21T12:02:00.000Z',
      lastSnapshotWarningMarker: null,
      lastReservedLease: {
        leaseId: 'lease-1',
        fiscalYear: '2026',
        remainingCount: 12,
        expiresAt: '2026-12-31T23:59:59.000Z',
      },
    };

    storage.saveSettings(settings);
    const loaded = storage.loadSettings(shopId);

    expect(loaded).toEqual(settings);
  });

  it('falls back to default disabled settings when persisted JSON is corrupted', () => {
    const shopId = 'shop-1';
    const deviceIdKey = `intelibill.offlineSales.deviceId.v1:${shopId}`;
    const settingsKey = `intelibill.offlineSales.deviceSettings.v1:${shopId}:device-1`;

    localStorage.setItem(deviceIdKey, 'device-1');
    localStorage.setItem(settingsKey, '{this is not json');

    const loaded = storage.loadSettings(shopId);

    expect(loaded).toEqual({
      shopId,
      deviceId: 'device-1',
      label: '',
      enabled: false,
      enabledAt: null,
      enabledByUserId: null,
      enabledByUserName: null,
      lastCompleteSnapshotAt: null,
      lastApiVerifiedAt: null,
      lastSnapshotWarningMarker: null,
      lastReservedLease: null,
    });
  });
});
