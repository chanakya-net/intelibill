import { invoiceErrorKey } from '../../../sales/utils/invoice-error-keys';
import { OfflineSalesDeviceSettings } from '../../../../core/storage/offline-sales-device-settings.storage';
import type { OfflinePackStatus } from '../../../../core/storage/offline-pack-status.storage';

export type OfflineReadinessState = 'enabled' | 'needsSetup';

export function getOfflineReadinessState(shopId: string, settings: OfflineSalesDeviceSettings | null): OfflineReadinessState {
  if (!shopId || !settings?.deviceId) return 'needsSetup';
  return settings.enabled ? 'enabled' : 'needsSetup';
}

export function formatOfflineSnapshotAgeLabel(completedAt: string | null | undefined): string {
  if (!completedAt) return '';
  const ms = Date.now() - Date.parse(completedAt);
  if (!Number.isFinite(ms) || ms < 0) return '';
  const minutes = Math.floor(ms / 60000);
  if (minutes < 60) return `${minutes}m`;
  const hours = Math.floor(minutes / 60);
  if (hours < 48) return `${hours}h`;
  const days = Math.floor(hours / 24);
  return `${days}d`;
}

export function getOfflineInvoiceRemainingCount(settings: OfflineSalesDeviceSettings | null): number | null {
  return settings?.lastReservedLease?.remainingCount ?? null;
}

export function offlineEnablementErrorKeyForReason(reason?: string, errorCode?: string, packFailure?: string): string {
  // A lease the server refused for the shop's GST registration says what the owner must fix.
  const invoiceKey = invoiceErrorKey(errorCode);
  if (invoiceKey) return invoiceKey;

  if (reason === 'APP_PACK_INCOMPLETE') {
    return packFailure === 'STORAGE_LOW'
      ? 'offlineSalesDevice.errors.appPackStorageLow'
      : 'offlineSalesDevice.errors.appPackIncomplete';
  }

  const keys: Record<string, string> = {
    API_UNREACHABLE: 'offlineSalesDevice.errors.apiUnreachable',
    SNAPSHOT_INCOMPLETE: 'offlineSalesDevice.errors.snapshotIncomplete',
    LEASE_UNAVAILABLE: 'offlineSalesDevice.errors.leaseUnavailable',
    ACTIVE_SHOP_MISMATCH: 'offlineSalesDevice.errors.enableFailed',
  };
  return keys[reason ?? ''] ?? 'offlineSalesDevice.errors.enableFailed';
}

/** The label for the step an enable is on (`OfflineSalesDeviceEnablementService.step`). */
export function offlineEnablementProgressKey(step: string | null | undefined): string {
  switch (step) {
    case 'appPack':
      return 'offlineSalesDevice.progress.appPack';
    case 'snapshot':
      return 'offlineSalesDevice.progress.snapshot';
    case 'lease':
      return 'offlineSalesDevice.progress.lease';
    default:
      return 'offlineSalesDevice.enabling';
  }
}

/** The pack's build, short enough to read out over the phone. */
export function formatOfflinePackVersion(status: OfflinePackStatus | null | undefined): string {
  return status?.buildId ? status.buildId.slice(0, 8) : '';
}
