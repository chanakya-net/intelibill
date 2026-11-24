import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { SESSION_PORT } from '@intelibill/api';
import type { Subscription } from 'rxjs';
import { formatIstExact } from './account-status-api.service';
import { type AccountDevicePermits, type DevicePermit, DevicePermitApi, devicePermitReaders, remaining } from './device-permit-api.service';

type Outcome = 'idle' | 'loading' | 'denied' | 'missing' | 'unavailable' | 'session';

/**
 * Read-only: what the merchant service observed about each device's offline permission, kept apart from what
 * it cannot observe (the device's own state) and what stays unknown (invoices not yet uploaded). Renders
 * nothing and asks nothing without a reading grant; the server re-checks the live grant on every read.
 */
@Component({
  selector: 'adm-device-permit-panel',
  templateUrl: './device-permit-panel.component.html',
  styleUrl: './device-permit-panel.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DevicePermitPanelComponent {
  readonly account = input.required<string>();
  readonly shops = input<readonly { readonly shopId: string; readonly shopName: string }[]>([]);
  private readonly api = inject(DevicePermitApi);
  private readonly session = inject(SESSION_PORT);
  protected readonly mayRead = computed(() => this.session.state() === 'authenticated'
    && this.session.capabilities().some(grant => (devicePermitReaders as readonly string[]).includes(grant)));
  protected readonly shop = signal('');
  private readonly observedShops = signal<readonly { readonly shopId: string; readonly shopName: string }[]>([]);
  protected readonly filterShops = computed(() => {
    const merged = new Map<string, { readonly shopId: string; readonly shopName: string }>();
    for (const item of this.shops()) merged.set(item.shopId, item);
    for (const item of this.observedShops()) if (!merged.has(item.shopId)) merged.set(item.shopId, item);
    return [...merged.values()];
  });
  protected readonly result = signal<AccountDevicePermits | null>(null);
  protected readonly outcome = signal<Outcome>('idle');
  protected readonly exact = formatIstExact;
  protected readonly limits = {
    LicenceEnd: 'The licence or its grace period ends then',
    VerificationFreshness: 'Licence verification was fresh for 48 hours only',
    MerchantLimit: 'An earlier merchant-side limit applies (sign-in, invoice numbers, stock snapshot or till day)',
  };
  private request?: Subscription;
  private generation = 0;

  constructor() {
    effect(() => {
      const [account, allowed] = [this.account(), this.mayRead()];
      untracked(() => {
        this.shop.set('');
        this.observedShops.set([]);
        this.read(account, allowed);
      });
    });
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
  }

  protected filter(shop: string) {
    this.shop.set(shop);
    this.read(this.account(), this.mayRead());
  }

  protected left(device: DevicePermit, evaluatedAt: string) {
    return remaining(evaluatedAt, device.knownPermitEnd!);
  }

  /** A late answer for an earlier account, shop or grant is dropped. */
  private read(account: string, allowed: boolean) {
    this.request?.unsubscribe();
    const generation = ++this.generation;
    this.result.set(null);
    this.outcome.set(allowed ? 'loading' : 'idle');
    if (!allowed) return;
    const selectedShop = this.shop() || null;
    this.request = this.api.read(account, selectedShop).subscribe({
      next: result => {
        if (generation !== this.generation) return;
        if (!selectedShop) {
          this.observedShops.set([...new Map(result.devices.map(
            item => [item.shopId, { shopId: item.shopId, shopName: item.shopName }] as const,
          )).values()]);
        }
        this.result.set(result);
        this.outcome.set('idle');
      },
      error: (error: unknown) => {
        if (generation !== this.generation) return;
        const code = error instanceof HttpErrorResponse ? error.status : 0;
        this.outcome.set(code === 401 ? 'session' : code === 403 ? 'denied' : code === 404 ? 'missing' : 'unavailable');
      },
    });
  }
}
