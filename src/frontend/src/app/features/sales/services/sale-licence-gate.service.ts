import { HttpErrorResponse } from '@angular/common/http';
import { computed, inject, Injectable, Injector, signal } from '@angular/core';
import { problemCode } from '@intelibill/api';
import { firstValueFrom } from 'rxjs';
import type { TradingAuthorityFailure } from '../../billing/status/subscription-status.models';

import { ShopContextService } from '../../../core/context/shop-context.service';
import type { ShopScope } from '../../../core/context/shop-scope';
import {
  SubscriptionStatusService,
  TradingAuthorityReadError,
} from '../../billing/status/subscription-status.service';

const messagePrefix = 'billing.status.tradingAuthority.';

export const SALE_LICENCE_ERROR_KEYS: Readonly<Record<string, string>> = {
  'Licence.Expired': messagePrefix + 'expired',
  'Licence.PendingActivation': messagePrefix + 'pending',
  'Licence.TradingRestricted': messagePrefix + 'restricted',
  'Licence.SecuritySuspended': messagePrefix + 'restricted',
  'Licence.VerificationUnavailable': messagePrefix + 'unavailable',
  'Licence.Busy': messagePrefix + 'busy',
};

/** Offers a local checkout hint. The sale transaction still decides whether the operation can post. */
@Injectable({ providedIn: 'root' })
export class SaleLicenceGateService {
  private readonly injector = inject(Injector);
  private readonly context = inject(ShopContextService);
  private readonly denial = signal<{ scope: ShopScope; key: string } | null>(null);
  private readonly checkingScope = signal<ShopScope | null>(null);
  private version = 0;

  readonly checking = computed(() => !!this.checkingScope() && this.checkingScope() === this.context.scope());
  readonly blockingMessage = computed(() => {
    const denial = this.denial();
    return denial && denial.scope === this.context.scope() ? denial.key : null;
  });

  async refresh(): Promise<void> {
    const scope = this.context.scope();
    if (!scope?.shopId) return;
    const version = ++this.version;
    this.checkingScope.set(scope);
    try {
      const status = this.injector.get(SubscriptionStatusService);
      const result = await firstValueFrom(status.tradingAuthority(scope.shopId));
      if (this.isCurrent(scope, version) && result?.schemaVersion === 1 &&
          result.shopId === scope.shopId && typeof result.enforced === 'boolean' &&
          Number.isFinite(Date.parse(result.authorityVerifiedAt)) && Number.isFinite(Date.parse(result.validUntil))) {
        this.denial.set(null);
      }
    } catch (error) {
      if (!this.isCurrent(scope, version) || !(error instanceof TradingAuthorityReadError)) return;
      if (error.enforced === false) this.denial.set(null);
      if (error.enforced === true) this.applyReadDenial(scope, error.failure);
    } finally {
      if (version === this.version) this.checkingScope.set(null);
    }
  }

  reportRefusal(error: unknown, scope: ShopScope | null): void {
    if (!scope || scope !== this.context.scope() || !(error instanceof HttpErrorResponse)) return;
    const code = problemCode(error.error);
    const key = code && Object.hasOwn(SALE_LICENCE_ERROR_KEYS, code) ? SALE_LICENCE_ERROR_KEYS[code] : null;
    if (!key || code === 'Licence.Busy') return;

    // A read started before this POST's decision can contain an older grant.
    this.version++;
    this.checkingScope.set(null);
    this.denial.set({ scope, key });
  }

  private applyReadDenial(scope: ShopScope, failure: TradingAuthorityFailure): void {
    if (failure === 'expired' || failure === 'restricted' || failure === 'pending' || failure === 'unavailable') {
      this.denial.set({ scope, key: messagePrefix + failure });
    }
  }

  private isCurrent(scope: ShopScope, version: number): boolean {
    return scope === this.context.scope() && version === this.version;
  }
}
