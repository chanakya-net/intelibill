import { Injectable } from '@angular/core';
import { Observable, Subject } from 'rxjs';
import { filter } from 'rxjs/operators';

import { RealtimeHubConnection } from './realtime-hub-connection';

export interface ShopUpdatePayload {
  readonly eventType: string;
  readonly shopId: string;
  readonly changedIds: readonly string[];
  readonly occurredOn: string;
}

@Injectable({ providedIn: 'root' })
export class ShopUpdatesSignalRService extends RealtimeHubConnection<ShopUpdatePayload> {
  protected readonly hubPath = '/hubs/shop-updates';
  protected readonly eventName = 'ShopUpdated';
  protected readonly logPrefix = '[ShopUpdatesSignalRService]';

  private readonly updateSubject = new Subject<ShopUpdatePayload>();

  readonly authorityChanged$ = this.updateSubject.pipe(filter((event) => event.eventType === 'LicenceAuthorityChanged'));

  readonly updates$: Observable<ShopUpdatePayload> = this.updateSubject.asObservable().pipe(
    filter((event) => event.eventType !== 'LicenceAuthorityChanged' && !!this.authService.session()?.activeShopId),
  );

  protected override handle(payload: ShopUpdatePayload): void {
    this.updateSubject.next(payload);
  }
}
