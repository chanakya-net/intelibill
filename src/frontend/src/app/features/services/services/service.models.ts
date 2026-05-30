export interface Service {
  readonly serviceId: string;
  readonly code: string;
  readonly name: string;
  readonly description: string | null;
  readonly price: number;
  readonly hsnCode: string | null;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly isActive: boolean;
}

export interface ServiceQuery {
  readonly search: string;
  readonly includeInactive: boolean;
}

export type ServiceStatusFilter = 'all' | 'active' | 'inactive';

export interface ServiceSummary {
  readonly totalServices: number;
  readonly activeServices: number;
  readonly inactiveServices: number;
  readonly totalValue: number;
}

export interface AddServiceRequest {
  readonly name: string;
  readonly description: string | null;
  readonly price: number;
  readonly hsnCode: string | null;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly isActive: boolean;
}

export interface UpdateServiceRequest {
  readonly name: string;
  readonly description: string | null;
  readonly price: number;
  readonly hsnCode: string | null;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
}
