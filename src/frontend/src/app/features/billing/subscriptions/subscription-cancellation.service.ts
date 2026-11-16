import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import type { SubscriptionCommercial } from '../status/subscription-status.models';

export interface CancellationSummary {
  readonly changeId: string;
  readonly version: number;
  readonly effectiveAt: string;
  readonly policyVersion: string;
}

export interface CancellationDetail extends Omit<CancellationSummary, 'changeId'> {
  readonly schemaVersion: 1;
  readonly accountId: string;
  readonly termId: string;
  readonly changeId: string | null;
  readonly sourceHash: string | null;
  readonly affectedShopSetHash: string;
  readonly affectedShopCount: number;
  readonly affectedShops: readonly { readonly shopId: string; readonly shopName: string }[];
  readonly nextCursor: string | null;
  readonly basis: 'Paid';
  readonly commercial: SubscriptionCommercial;
  readonly noRefund: true;
  readonly noGrace: true;
}

export interface CancellationCommand {
  readonly operationKey: string;
  readonly expectedVersion: number;
  readonly acceptedEffectiveAt: string;
  readonly sourceHash: string;
  readonly affectedShopSetHash: string;
  readonly affectedShopCount: number;
  readonly policyVersion: string;
  readonly confirmed: true;
}

export interface CancellationOutcome {
  readonly schemaVersion: 1;
  readonly accountId: string;
  readonly termId: string;
  readonly operationKey: string;
  readonly cancellation: CancellationSummary;
  readonly affectedShopCount: number;
}

export interface WithdrawalCommand {
  readonly operationKey: string;
  readonly currentVersion: number;
  readonly changeId: string;
  readonly acceptedEffectiveAt: string;
  readonly confirmed: true;
}

export interface WithdrawalOutcome {
  readonly schemaVersion: 1;
  readonly accountId: string;
  readonly termId: string;
  readonly operationKey: string;
  readonly withdrawnChangeId: string;
  readonly version: number;
  readonly effectiveAt: string;
  readonly withdrawnAt: string;
  readonly affectedShopCount: number;
}

@Injectable({ providedIn: 'root' })
export class SubscriptionCancellationService {
  private readonly http = inject(HttpClient);

  async detail(account: string, term: string, current: () => boolean): Promise<CancellationDetail> {
    const path = this.path(account, term);
    const first = await firstValueFrom(this.http.get<CancellationDetail>(path));
    const shops = new Map(first.affectedShops.map(shop => [shop.shopId, shop]));
    const cursors = new Set<string>();
    let cursor = first.nextCursor;
    while (cursor) {
      if (!current() || cursors.has(cursor) || shops.size >= first.affectedShopCount) throw new Error('Invalid cancellation page');
      cursors.add(cursor);
      const page = await firstValueFrom(this.http.get<CancellationDetail>(path, { params: { after: cursor } }));
      if (!this.sameSource(first, page)) throw new Error('Cancellation scope changed');
      for (const shop of page.affectedShops) {
        if (shops.has(shop.shopId)) throw new Error('Repeated cancellation shop');
        shops.set(shop.shopId, shop);
      }
      cursor = page.nextCursor;
    }
    if (first.accountId !== account || first.termId !== term || shops.size !== first.affectedShopCount)
      throw new Error('Incomplete cancellation scope');
    return { ...first, affectedShops: [...shops.values()], nextCursor: null };
  }

  schedule(account: string, term: string, command: CancellationCommand) {
    return firstValueFrom(this.http.post<CancellationOutcome>(this.path(account, term), command));
  }

  recover(account: string, term: string, operation: string) {
    return firstValueFrom(this.http.get<CancellationOutcome>(
      this.path(account, term) + '/operations/' + encodeURIComponent(operation)));
  }

  withdraw(account: string, term: string, command: WithdrawalCommand) {
    return firstValueFrom(this.http.post<WithdrawalOutcome>(this.path(account, term) + '/withdrawal', command));
  }

  recoverWithdrawal(account: string, term: string, operation: string) {
    return firstValueFrom(this.http.get<WithdrawalOutcome>(
      this.path(account, term) + '/withdrawal/operations/' + encodeURIComponent(operation)));
  }

  private path(account: string, term: string) {
    return `${API_BASE_URL}/billing/accounts/${encodeURIComponent(account)}/subscriptions/${encodeURIComponent(term)}/cancellation`;
  }

  private sameSource(first: CancellationDetail, page: CancellationDetail) {
    return first.accountId === page.accountId && first.termId === page.termId && first.changeId === page.changeId
      && first.version === page.version && first.effectiveAt === page.effectiveAt && first.sourceHash === page.sourceHash
      && first.policyVersion === page.policyVersion && first.affectedShopSetHash === page.affectedShopSetHash
      && first.affectedShopCount === page.affectedShopCount;
  }
}
