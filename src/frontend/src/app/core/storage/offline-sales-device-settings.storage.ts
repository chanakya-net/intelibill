import { Injectable } from '@angular/core';

export interface OfflineSalesDeviceSettings {
  readonly shopId: string;
  readonly deviceId: string;
  readonly label: string;
  readonly enabled: boolean;
  readonly enabledAt: string | null;
  readonly enabledByUserId: string | null;
  readonly enabledByUserName: string | null;
  readonly lastCompleteSnapshotAt: string | null;
  readonly lastApiVerifiedAt: string | null;
  readonly lastSnapshotWarningMarker: string | null;
  readonly lastReservedLease: {
    readonly leaseId: string;
    readonly fiscalYear: string;
    readonly remainingCount: number;
    readonly expiresAt: string;
  } | null;
}

const DEVICE_ID_KEY_PREFIX = 'intelibill.offlineSales.deviceId.v1';
const SETTINGS_KEY_PREFIX = 'intelibill.offlineSales.deviceSettings.v1';

function safeJsonParse<T>(value: string | null): T | null {
  if (!value) return null;
  try {
    return JSON.parse(value) as T;
  } catch {
    return null;
  }
}

function createDeviceId(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }

  const rand = Math.random().toString(16).slice(2);
  return `device-${Date.now().toString(16)}-${rand}`;
}

@Injectable({ providedIn: 'root' })
export class OfflineSalesDeviceSettingsStorage {
  getOrCreateDeviceId(shopId: string): string {
    if (!shopId || typeof localStorage === 'undefined') {
      return '';
    }

    const key = this.buildDeviceIdKey(shopId);
    const existing = localStorage.getItem(key);
    if (existing) return existing;

    const created = createDeviceId();
    localStorage.setItem(key, created);
    return created;
  }

  loadSettings(shopId: string): OfflineSalesDeviceSettings | null {
    if (!shopId || typeof localStorage === 'undefined') {
      return null;
    }

    const deviceId = this.getOrCreateDeviceId(shopId);
    if (!deviceId) return null;

    const raw = safeJsonParse<OfflineSalesDeviceSettings>(localStorage.getItem(this.buildSettingsKey(shopId, deviceId)));
    if (!raw) {
      return {
        shopId,
        deviceId,
        label: '',
        enabled: false,
        enabledAt: null,
        enabledByUserId: null,
        enabledByUserName: null,
        lastCompleteSnapshotAt: null,
        lastApiVerifiedAt: null,
        lastSnapshotWarningMarker: null,
        lastReservedLease: null,
      };
    }

    return {
      shopId,
      deviceId,
      label: raw.label ?? '',
      enabled: !!raw.enabled,
      enabledAt: raw.enabledAt ?? null,
      enabledByUserId: raw.enabledByUserId ?? null,
      enabledByUserName: raw.enabledByUserName ?? null,
      lastCompleteSnapshotAt: raw.lastCompleteSnapshotAt ?? null,
      lastApiVerifiedAt: raw.lastApiVerifiedAt ?? null,
      lastSnapshotWarningMarker: raw.lastSnapshotWarningMarker ?? null,
      lastReservedLease: raw.lastReservedLease ?? null,
    };
  }

  saveSettings(settings: OfflineSalesDeviceSettings): void {
    if (!settings?.shopId || !settings?.deviceId || typeof localStorage === 'undefined') {
      return;
    }

    localStorage.setItem(this.buildSettingsKey(settings.shopId, settings.deviceId), JSON.stringify(settings));
  }

  updateSettings(shopId: string, update: (current: OfflineSalesDeviceSettings) => OfflineSalesDeviceSettings): OfflineSalesDeviceSettings | null {
    const current = this.loadSettings(shopId);
    if (!current) return null;

    const next = update(current);
    this.saveSettings(next);
    return next;
  }

  private buildDeviceIdKey(shopId: string): string {
    return `${DEVICE_ID_KEY_PREFIX}:${shopId}`;
  }

  private buildSettingsKey(shopId: string, deviceId: string): string {
    return `${SETTINGS_KEY_PREFIX}:${shopId}:${deviceId}`;
  }
}

