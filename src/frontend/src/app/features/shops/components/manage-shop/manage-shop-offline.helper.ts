import { OfflineSalesDeviceSettings } from '../../../../core/storage/offline-sales-device-settings.storage';

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

export function offlineEnablementErrorKeyForReason(reason?: string): string {
  const keys: Record<string, string> = {
    API_UNREACHABLE: 'offlineSalesDevice.errors.apiUnreachable',
    SNAPSHOT_INCOMPLETE: 'offlineSalesDevice.errors.snapshotIncomplete',
    LEASE_UNAVAILABLE: 'offlineSalesDevice.errors.leaseUnavailable',
    ACTIVE_SHOP_MISMATCH: 'offlineSalesDevice.errors.enableFailed',
  };
  return keys[reason ?? ''] ?? 'offlineSalesDevice.errors.enableFailed';
}
