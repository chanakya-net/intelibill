import type { InvoiceDocument } from '../../sales/services/sale.models';

export type ShopSetupStepId = 'identity' | 'business' | 'tax' | 'catalog' | 'stock' | 'sample' | 'bank';
export type ShopSetupStepState = 'complete' | 'incomplete' | 'optional' | 'notApplicable';

export interface ShopSetupStep {
  readonly id: ShopSetupStepId;
  readonly state: ShopSetupStepState;
  readonly reasons: readonly string[];
}

export interface ShopSetupPreferences {
  readonly version: number;
  readonly showOnStartup: boolean;
  readonly sampleReceiptAcknowledgedAt: string | null;
}

export interface ShopSetupStatus {
  readonly shopId: string;
  readonly contractVersion: 1;
  readonly canTrade: boolean;
  readonly steps: readonly ShopSetupStep[];
  readonly blockingReasons: readonly string[];
  readonly permissions: {
    readonly canEditShop: boolean;
    readonly canEditCatalog: boolean;
    readonly canSavePreferences: boolean;
  };
  readonly preferences: ShopSetupPreferences;
}

export interface ShopSetupSample {
  readonly isSample: true;
  readonly document: InvoiceDocument;
  readonly lineType: 'Goods' | 'Service';
  readonly generatedAt: string;
}

export interface SaveShopSetupPreferences {
  readonly expectedVersion: number;
  readonly showOnStartup: boolean;
  readonly acknowledgeSample: boolean;
}
