import { problemCode } from '@intelibill/api';

import type { UpdateShopRequest } from './shop.service';
import { OPERATING_ACCESS_ERROR_KEYS } from '../../billing/status/operating-access-hint.service';

export type ShopProfileField = keyof UpdateShopRequest;

export interface ShopSetupAccess {
  readonly contractVersion: 1;
  readonly shopId: string;
  readonly enforced: boolean;
  readonly status: string;
  readonly writableProfileFields: readonly ShopProfileField[];
  readonly canPrepareTax: boolean;
  readonly canSavePreferences: boolean;
  readonly canEditOperatingSettings: boolean;
  readonly hasOperatingAccess: boolean;
  readonly canCreateAdditionalShop: boolean;
}

const securityKey = 'billing.status.suspension.enforcedSecurity';
const unavailable = OPERATING_ACCESS_ERROR_KEYS['Licence.VerificationUnavailable'];

export function setupLicenceError(error: unknown): string | undefined {
  const code = problemCode(error);
  return code === 'Licence.SecuritySuspended' ? securityKey : code ? OPERATING_ACCESS_ERROR_KEYS[code] : undefined;
}

/** A definitive licence refusal for one shop. Contention is retryable and is not one. */
export interface ShopLicenceRefusal {
  readonly shopId: string;
  readonly securitySuspended: boolean;
  readonly errorMessage: string;
}

export function isSecuritySuspension(error: unknown): boolean {
  return problemCode(error) === 'Licence.SecuritySuspended';
}

export function shopLicenceRefusal(shopId: string, error: unknown): ShopLicenceRefusal | null {
  const errorMessage = setupLicenceError(error);
  if (!errorMessage || problemCode(error) === 'Licence.Busy') return null;
  return { shopId, securitySuspended: isSecuritySuspension(error), errorMessage };
}

export function shopSetupAccessFor(shopId: string | null, access?: ShopSetupAccess | null): ShopSetupAccess | null {
  return shopId && access?.shopId === shopId && access.contractVersion === 1 ? access : null;
}

export function shopSetupStatusKey(access: ShopSetupAccess | null): string | null {
  if (!access) return unavailable;
  switch (access.status) {
    case 'ObserveOnly': return null;
    case 'Pending': return OPERATING_ACCESS_ERROR_KEYS['Licence.PendingActivation'];
    case 'Expired': return OPERATING_ACCESS_ERROR_KEYS['Licence.Expired'];
    case 'TradingRestricted': return OPERATING_ACCESS_ERROR_KEYS['Licence.TradingRestricted'];
    case 'SecuritySuspended': return securityKey;
    case 'Trial': return 'billing.status.bases.Trial';
    case 'Grace': return 'billing.status.states.Grace';
    default: return access.hasOperatingAccess ? 'billing.status.states.Active' : unavailable;
  }
}
