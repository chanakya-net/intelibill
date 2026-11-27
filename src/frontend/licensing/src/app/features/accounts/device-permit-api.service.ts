import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { EMPTY, type Observable, expand, reduce } from 'rxjs';

/** Provider grants that may read device permission; Billing and Platform Owner alone may not. */
export const devicePermitReaders = ['support', 'licensing-manager', 'security-administrator'] as const;

/**
 * One device as the merchant service observed it. Every instant is that service's own clock:
 * `lastSynchronizedAt` is only an offline upload it received (null when none was observed) and is never
 * a permit's time; `knownPermitEnd` is the latest end among the permits it issued.
 */
export interface DevicePermit {
  readonly shopId: string;
  readonly shopName: string;
  readonly deviceRef: string;
  readonly lastContactAt: string;
  readonly lastSynchronizedAt: string | null;
  readonly lastPermitIssuedAt: string | null;
  readonly outstanding: boolean;
  readonly knownPermitEnd: string | null;
  readonly outstandingPermits: number;
  readonly limit: 'LicenceEnd' | 'VerificationFreshness' | 'MerchantLimit' | null;
  readonly accountRevision: number | null;
  readonly shopRevision: number | null;
  readonly authorityVerifiedAt: string | null;
}

export interface AccountDevicePermits {
  readonly accountId: string;
  readonly evaluatedAt: string;
  /** Always `Unknown`: invoices a device has not uploaded are never visible to the provider. */
  readonly disconnectedQueues: 'Unknown';
  /** The account's earliest reported observation; null when nothing has been reported. */
  readonly observedSince: string | null;
  readonly devices: readonly DevicePermit[];
}

interface DevicePermitPage extends AccountDevicePermits {
  readonly nextCursor: string | null;
}

@Injectable({ providedIn: 'root' })
export class DevicePermitApi {
  private readonly http = inject(HttpClient);

  /** Every page in order, so a device is never silently left out. */
  read(account: string, shop?: string | null): Observable<AccountDevicePermits> {
    const page = (after: string | null) => this.http.get<DevicePermitPage>(
      `/api/admin/accounts/${encodeURIComponent(account)}/device-permits`,
      { params: { ...(shop ? { shop } : {}), ...(after ? { after } : {}) } });
    return page(null).pipe(
      expand(result => result.nextCursor ? page(result.nextCursor) : EMPTY),
      reduce<DevicePermitPage, AccountDevicePermits | null>((all, result) => all
        ? { ...all, devices: [...all.devices, ...result.devices] }
        : { accountId: result.accountId, evaluatedAt: result.evaluatedAt, disconnectedQueues: result.disconnectedQueues,
          observedSince: result.observedSince, devices: result.devices }, null),
    ) as Observable<AccountDevicePermits>;
  }
}

/** Time left before `end`, measured from the server's own evaluation instant and never the browser clock. */
export function remaining(evaluatedAt: string, end: string): string {
  const minutes = Math.floor((Date.parse(end) - Date.parse(evaluatedAt)) / 60_000);
  if (minutes < 1) return 'less than 1 min';
  const [days, hours] = [Math.floor(minutes / 1440), Math.floor(minutes % 1440 / 60)];
  return [days ? `${days} d` : '', hours ? `${hours} h` : '', `${minutes % 60} min`].filter(Boolean).join(' ');
}
