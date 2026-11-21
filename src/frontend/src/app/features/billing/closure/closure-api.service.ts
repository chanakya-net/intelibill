import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface ClosureReview {
  readonly shopId: string;
  readonly shopName: string;
  readonly accountId: string;
  readonly affiliationRequestId: string;
  readonly closureVersion: number;
  readonly ownerVersion: number;
  readonly observedAt: string;
  readonly workDigest: string;
  readonly openTills: number;
  readonly unresolvedCases: number;
  readonly observedSubmissions: number;
  readonly linkedSubmissions: number;
  readonly incompleteIntakes: number;
  readonly unreadableIntakes: number;
  readonly observedDevices: number;
  readonly devicesTruncated: boolean;
  readonly disconnectedQueuesUnknown: true;
  readonly financialTreatment: 'NoFinancialChange';
  readonly devices: readonly {
    readonly reference: string;
    readonly lastContactAt: string;
    readonly recordedLeaseEndsAt: string | null;
    readonly permitEvidence: 'Unavailable';
  }[];
}

export interface AccountClosureProposal {
  readonly id: string;
  readonly accountId: string;
  readonly scopeDigest: string;
  readonly expiresAt: string;
  readonly shops: readonly { readonly shopId: string; readonly shopName: string; readonly consentRecorded: boolean }[];
}

export interface OwnerClosureProposal extends Omit<AccountClosureProposal, 'shops'> {
  readonly shopCount: number;
  readonly target: { readonly shopId: string; readonly affiliationRequestId: string; readonly closureVersion: number };
  readonly consentRecorded: boolean;
}

export interface ClosureSaved {
  readonly id: string;
  readonly accountId: string;
  readonly operationKey: string;
  readonly scope: 'Shop' | 'Account';
  readonly financialTreatment: 'NoFinancialChange';
  readonly shops: readonly { readonly shopId: string }[];
}

export interface ClosureCommand {
  readonly operationKey: string;
  readonly expectedVersion: number;
  readonly ownerVersion: number;
  readonly workDigest: string;
  readonly reviewedKnownWork: true;
  readonly consideredDeviceQueues: true;
  readonly financialTreatment: 'NoFinancialChange';
}

export interface AccountClosureCommand {
  readonly operationKey: string;
  readonly scopeDigest: string;
  readonly confirmed: true;
}

@Injectable({ providedIn: 'root' })
export class ClosureApi {
  private readonly http = inject(HttpClient);

  review(shop: string) {
    return firstValueFrom(this.http.get<ClosureReview>(`${this.shop(shop)}/closure-preview`));
  }

  ownerProposal(shop: string) {
    return firstValueFrom(this.http.get<OwnerClosureProposal>(`${this.shop(shop)}/account-closure`));
  }

  close(shop: string, command: ClosureCommand) {
    return firstValueFrom(this.http.post<ClosureSaved>(`${this.shop(shop)}/closure`, command));
  }

  consent(shop: string, proposal: string, command: ClosureCommand) {
    return firstValueFrom(this.http.post<OwnerClosureProposal>(
      `${this.shop(shop)}/account-closure/${encodeURIComponent(proposal)}`, command));
  }

  accountProposal(account: string) {
    return firstValueFrom(this.http.get<AccountClosureProposal>(this.account(account)));
  }

  propose(account: string, operationKey: string) {
    return firstValueFrom(this.http.post<AccountClosureProposal>(this.account(account),
      { operationKey, financialTreatment: 'NoFinancialChange' }));
  }

  confirm(account: string, proposal: string, command: AccountClosureCommand) {
    return firstValueFrom(this.http.post<ClosureSaved>(`${this.account(account)}/${encodeURIComponent(proposal)}/confirm`, command));
  }

  private shop(shop: string) {
    return `/api/licensing/shops/${encodeURIComponent(shop)}`;
  }

  private account(account: string) {
    return `/api/billing/accounts/${encodeURIComponent(account)}/closures`;
  }
}
