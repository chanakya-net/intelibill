import { DestroyRef, Injectable, inject } from '@angular/core';
import { Subject } from 'rxjs';

/** Settings invalidate verified capability in this tab and other open tabs. */
@Injectable({ providedIn: 'root' })
export class TillSettingsEvents {
  private readonly changes = new Subject<string>();
  readonly changed$ = this.changes.asObservable();
  private readonly channel =
    typeof BroadcastChannel === 'undefined'
      ? null
      : new BroadcastChannel('intelibill-till-settings');
  constructor() {
    if (this.channel)
      this.channel.onmessage = (event) => {
        if (typeof event.data === 'string') this.changes.next(event.data);
      };
    inject(DestroyRef).onDestroy(() => {
      this.channel?.close();
      this.changes.complete();
    });
  }
  publish(shopId: string) {
    this.changes.next(shopId);
    this.channel?.postMessage(shopId);
  }
}
