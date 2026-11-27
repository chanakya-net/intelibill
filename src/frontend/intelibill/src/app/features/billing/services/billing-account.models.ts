export type BillingAccountRole = 'Owner' | 'Manager';

export interface BillingAccount {
  readonly accountId: string;
  readonly name: string;
  readonly role: BillingAccountRole;
  readonly createdAt: string;
}

export interface CreateBillingAccountRequest {
  readonly operationKey: string;
  readonly name: string;
}

export interface CreatedBillingAccount {
  readonly account: BillingAccount;
  /** False when the server answered a retry of an operation it had already completed. */
  readonly created: boolean;
}

/** An explicit creation attempt, frozen before the request so every retry repeats it exactly. */
export interface BillingAccountDraft {
  readonly ownerUserId: string;
  readonly operationKey: string;
  readonly name: string;
}
