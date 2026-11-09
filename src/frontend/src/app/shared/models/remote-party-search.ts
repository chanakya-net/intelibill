import { signal } from '@angular/core';
import type { Observable, Subscription } from 'rxjs';

/** Each caller owns one search and a separate selected value. Epoch fences also protect A→B→A. */
export class RemotePartySearch<T> {
  readonly items = signal<readonly T[]>([]);
  readonly loading = signal(false);
  readonly error = signal(false);
  private revision = 0;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private subscription: Subscription | undefined;
  constructor(
    private readonly fetch: (search: string) => Observable<{ items: readonly T[] }>,
    private readonly epoch: () => number,
    private readonly onItems?: (items: readonly T[]) => void,
  ) {}
  search(search: string): void {
    this.cancel();
    const revision = this.revision;
    const epoch = this.epoch();
    this.items.set([]);
    this.onItems?.([]);
    this.error.set(false);
    this.loading.set(true);
    this.timer = setTimeout(() => {
      if (revision !== this.revision || epoch !== this.epoch()) {
        this.loading.set(false);
        return;
      }
      this.subscription = this.fetch(search.trim()).subscribe({
        next: (page) => {
          if (revision === this.revision && epoch === this.epoch()) {
            this.items.set(page.items);
            this.onItems?.(page.items);
            this.loading.set(false);
          }
        },
        error: () => {
          if (revision === this.revision && epoch === this.epoch()) {
            this.error.set(true);
            this.loading.set(false);
          }
        },
      });
    }, 250);
  }
  cancel(): void {
    ++this.revision;
    clearTimeout(this.timer);
    this.subscription?.unsubscribe();
    this.loading.set(false);
  }
  destroy(): void {
    this.cancel();
  }
}
