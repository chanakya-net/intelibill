import { HttpErrorResponse } from '@angular/common/http';
import { computed, inject, Injectable, Injector, signal } from '@angular/core';
import { problemCode } from '@intelibill/api';
import { firstValueFrom } from 'rxjs';
import type { TradingAuthorityFailure } from './subscription-status.models';

import { ShopContextService } from '../../../core/context/shop-context.service';
import type { ShopScope } from '../../../core/context/shop-scope';
import {
  SubscriptionStatusService,
  TradingAuthorityReadError,
} from './subscription-status.service';

const messagePrefix = 'billing.status.tradingAuthority.';

export const OPERATING_ACCESS_ERROR_KEYS: Readonly<Record<string, string>> = {
  'Licence.Expired': messagePrefix + 'expired',
  'Licence.PendingActivation': messagePrefix + 'pending',
  'Licence.TradingRestricted': messagePrefix + 'restricted',
  'Licence.SecuritySuspended': messagePrefix + 'restricted',
  'Licence.VerificationUnavailable': messagePrefix + 'unavailable',
  'Licence.Busy': messagePrefix + 'busy',
};

export function operatingAccessErrorKey(error: unknown): string | null {
  const code = error instanceof HttpErrorResponse ? problemCode(error.error) : null;
  return code && Object.hasOwn(OPERATING_ACCESS_ERROR_KEYS, code) ? OPERATING_ACCESS_ERROR_KEYS[code] : null;
}

/** Scoped hint state. The source transaction makes the final decision. */
export abstract class OperatingAccessHint {
  private readonly injector = inject(Injector);
  private readonly context = inject(ShopContextService);
  private readonly denial = signal<{ scope: ShopScope; key: string } | null>(null);
  private readonly checkingScope = signal<ShopScope | null>(null);
  private version = 0;

  readonly scope = this.context.scope;

  readonly checking = computed(() => !!this.checkingScope() && this.checkingScope() === this.context.scope());
  readonly blockingMessage = computed(() => {
    const denial = this.denial();
    return denial && denial.scope === this.context.scope() ? denial.key : null;
  });

  async refresh(): Promise<boolean> {
    const scope = this.context.scope();
    if (!scope?.shopId) return false;
    const version = ++this.version;
    this.checkingScope.set(scope);
    try {
      const status = this.injector.get(SubscriptionStatusService);
      const result = await firstValueFrom(status.tradingAuthority(scope.shopId));
      if (this.isCurrent(scope, version) && result?.schemaVersion === 1 &&
          result.shopId === scope.shopId && typeof result.enforced === 'boolean' &&
          Number.isFinite(Date.parse(result.authorityVerifiedAt)) && Number.isFinite(Date.parse(result.validUntil))) {
        this.denial.set(null);
        return true;
      }
      return false;
    } catch (error) {
      if (!this.isCurrent(scope, version) || !(error instanceof TradingAuthorityReadError)) return false;
      if (error.enforced === false) {
        this.denial.set(null);
        return true;
      }
      return error.enforced === true && this.applyReadDenial(scope, error.failure);
    } finally {
      if (version === this.version) this.checkingScope.set(null);
    }
  }

  reportRefusal(error: unknown, scope: ShopScope | null): void {
    if (!scope || scope !== this.scope()) return;
    const key = operatingAccessErrorKey(error);
    if (!key || key === OPERATING_ACCESS_ERROR_KEYS['Licence.Busy']) return;

    // A read started before this POST's decision can contain an older grant.
    this.version++;
    this.checkingScope.set(null);
    this.denial.set({ scope, key });
  }

  private applyReadDenial(scope: ShopScope, failure: TradingAuthorityFailure): boolean {
    if (failure === 'expired' || failure === 'restricted' || failure === 'pending' || failure === 'unavailable') {
      this.denial.set({ scope, key: messagePrefix + failure });
      return true;
    }
    return false;
  }

  private isCurrent(scope: ShopScope, version: number): boolean {
    return scope === this.context.scope() && version === this.version;
  }
}

@Injectable({ providedIn: 'root' })
export class OperatingAccessHintService extends OperatingAccessHint {
  /** Persistent decisions go to the shared notice. Transient errors stay with their form. */
  creationError(error: unknown, scope: ShopScope | null, fallback: string): string | null {
    if (scope !== this.scope()) return null;
    const key = operatingAccessErrorKey(error);
    this.reportRefusal(error, scope);
    return key && key === this.blockingMessage() ? null : (key ?? fallback);
  }
}
