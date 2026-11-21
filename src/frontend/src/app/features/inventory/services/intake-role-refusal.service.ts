import { Injectable, Injector, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { ShopContextService } from '../../../core/context/shop-context.service';
import { ownerKey, type ScopeOwner, type ShopScope } from '../../../core/context/shop-scope';
import { ShopService } from '../../shops/services/shop.service';

export const INTAKE_ROLE_REFUSAL_STORAGE_KEY = 'intelibill.inventory.intakeRoleRefusal.v1';

/**
 * Remembers that the server refused stock intake to one user in one shop. The refusal outlives the
 * page and the cached session role: it ends only when a live membership read lists that user as
 * owner or manager of that shop again.
 */
@Injectable({ providedIn: 'root' })
export class IntakeRoleRefusalService {
  private readonly injector = inject(Injector);
  private readonly context = inject(ShopContextService);
  private readonly refused = signal<readonly string[]>(this.read());
  private readonly checkingKey = signal<string | null>(null);

  readonly checking = computed(() => {
    const scope = this.context.scope();
    return !!scope && this.checkingKey() === ownerKey(scope);
  });

  constructor() {
    if (typeof window === 'undefined') return;
    // Another tab recorded or lifted a refusal for the same browser profile.
    window.addEventListener('storage', event => {
      if (event.key === INTAKE_ROLE_REFUSAL_STORAGE_KEY) this.refused.set(this.read());
    });
  }

  isRefused(owner: ScopeOwner): boolean {
    return this.refused().includes(ownerKey(owner));
  }

  refuse(owner: ScopeOwner): void {
    const key = ownerKey(owner);
    if (!this.refused().includes(key)) this.write([...this.read().filter(other => other !== key), key]);
  }

  /** Lifts the refusal only on a live answer for the scope that asked. */
  async revalidate(scope: ShopScope): Promise<boolean> {
    const key = ownerKey(scope);
    if (!this.isRefused(scope) || this.checkingKey() === key) return false;

    this.checkingKey.set(key);
    try {
      // Resolved on use: only a recorded refusal ever needs the membership read.
      const memberships = await firstValueFrom(this.injector.get(ShopService).getMyShops());
      if (this.context.scope() !== scope || !Array.isArray(memberships)) return false;
      const role = memberships.find(shop => shop?.shopId === scope.shopId)?.role;
      if (typeof role !== 'string' || !['owner', 'manager'].includes(role.trim().toLowerCase())) return false;

      this.write(this.read().filter(other => other !== key));
      return true;
    } catch {
      return false;
    } finally {
      if (this.checkingKey() === key) this.checkingKey.set(null);
    }
  }

  private read(): readonly string[] {
    try {
      const stored: unknown = JSON.parse(localStorage.getItem(INTAKE_ROLE_REFUSAL_STORAGE_KEY) ?? '[]');
      return Array.isArray(stored) ? stored.filter((key): key is string => typeof key === 'string') : [];
    } catch {
      // Without storage the refusal still holds for this tab's lifetime.
      return this.refused?.() ?? [];
    }
  }

  private write(keys: readonly string[]): void {
    this.refused.set(keys);
    try {
      if (keys.length) localStorage.setItem(INTAKE_ROLE_REFUSAL_STORAGE_KEY, JSON.stringify(keys));
      else localStorage.removeItem(INTAKE_ROLE_REFUSAL_STORAGE_KEY);
    } catch {
      // Kept in memory only.
    }
  }
}
