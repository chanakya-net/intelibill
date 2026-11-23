import { Injectable, signal } from '@angular/core';

export interface OfflinePermitDisplaySummary {
  readonly leaseId: string;
  readonly grantId: string;
  readonly permitId: string;
  readonly snapshotId: string;
  readonly issuedAt: string;
  readonly notBefore: string;
  readonly validUntil: string;
  readonly limitingReason: string;
}

export type PermitRefreshNotice = 'verified' | 'unverified' | 'restricted' | 'suspended';

export interface OfflineSalesDeviceSettings {
  /** Display and request scheduling only. Issuance always verifies the IndexedDB permit. */
  readonly permitRefreshNotice?: PermitRefreshNotice;
  readonly permitAuthorityVerifiedAt?: string;
  readonly permitRetry?: { readonly leaseId: string; readonly failures: number; readonly after: number };
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
    readonly licenceGrantId?: string | null;
  } | null;
  readonly lastOfflinePermit?: OfflinePermitDisplaySummary | null;
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
  private readonly savedSignal = signal<OfflineSalesDeviceSettings | null>(null);
  readonly saved = this.savedSignal.asReadonly();

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
        lastOfflinePermit: null,
      };
    }

    return {
      shopId,
      deviceId,
      permitRefreshNotice: raw.permitRefreshNotice,
      permitAuthorityVerifiedAt: raw.permitAuthorityVerifiedAt,
      permitRetry: raw.permitRetry,
      label: raw.label ?? '',
      enabled: !!raw.enabled,
      enabledAt: raw.enabledAt ?? null,
      enabledByUserId: raw.enabledByUserId ?? null,
      enabledByUserName: raw.enabledByUserName ?? null,
      lastCompleteSnapshotAt: raw.lastCompleteSnapshotAt ?? null,
      lastApiVerifiedAt: raw.lastApiVerifiedAt ?? null,
      lastSnapshotWarningMarker: raw.lastSnapshotWarningMarker ?? null,
      lastReservedLease: raw.lastReservedLease ?? null,
      lastOfflinePermit: raw.lastOfflinePermit ?? null,
    };
  }

  saveSettings(settings: OfflineSalesDeviceSettings): void {
    if (!settings?.shopId || !settings?.deviceId || typeof localStorage === 'undefined') {
      return;
    }

    localStorage.setItem(this.buildSettingsKey(settings.shopId, settings.deviceId), JSON.stringify(settings));
    this.savedSignal.set(settings);
  }

  updateSettings(shopId: string, update: (current: OfflineSalesDeviceSettings) => OfflineSalesDeviceSettings): OfflineSalesDeviceSettings | null {
    const current = this.loadSettings(shopId);
    if (!current) return null;

    const next = update(current);
    this.saveSettings(next);
    return next;
  }

  /**
   * True when offline billing is enabled for any shop on this browser: the device then needs the pack.
   * Null when the settings cannot be read (storage blocked or throwing): unknown, never "no shop", so a
   * caller keeps the pack rather than drop it over a storage error.
   */
  anyShopEnabled(): boolean | null {
    try {
      if (typeof localStorage === 'undefined') {
        return null;
      }

      for (let index = 0; index < localStorage.length; index += 1) {
        const key = localStorage.key(index);
        if (key?.startsWith(`${SETTINGS_KEY_PREFIX}:`) && safeJsonParse<{ enabled?: unknown }>(localStorage.getItem(key))?.enabled === true) {
          return true;
        }
      }
      return false;
    } catch {
      return null;
    }
  }

  private buildDeviceIdKey(shopId: string): string {
    return `${DEVICE_ID_KEY_PREFIX}:${shopId}`;
  }

  private buildSettingsKey(shopId: string, deviceId: string): string {
    return `${SETTINGS_KEY_PREFIX}:${shopId}:${deviceId}`;
  }
}

