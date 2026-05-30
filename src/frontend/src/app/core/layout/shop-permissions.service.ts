import { computed, Injectable, inject } from '@angular/core';

import { AuthService } from '../auth/auth.service';

@Injectable({ providedIn: 'root' })
export class ShopPermissionsService {
  private readonly authService = inject(AuthService);
  private readonly session = this.authService.session;

  readonly activeShop = computed(() => {
    const session = this.session();
    const shops = session?.shops ?? [];
    if (shops.length === 0) {
      return null;
    }

    const activeShopId = session?.activeShopId ?? null;
    if (activeShopId) {
      return shops.find((shop) => shop.shopId === activeShopId) ?? null;
    }

    return shops.find((shop) => shop.isDefault) ?? shops[0] ?? null;
  });

  readonly activeShopId = computed(() => this.activeShop()?.shopId ?? null);
  private readonly activeRole = computed(() => (this.activeShop()?.role ?? '').trim().toLowerCase());
  private readonly hasActiveShop = computed(() => this.activeShop() !== null);

  readonly isOwnerOfActiveShop = computed(() => this.hasActiveShop() && this.activeRole() === 'owner');
  readonly isOwnerOrManagerOfActiveShop = computed(() => {
    if (!this.hasActiveShop()) {
      return false;
    }

    const role = this.activeRole();
    return role === 'owner' || role === 'manager';
  });

  readonly canManageSuppliers = computed(() => this.isOwnerOfActiveShop());

  readonly canManageCustomers = computed(() => {
    if (!this.hasActiveShop()) {
      return false;
    }

    return this.isOwnerOrManagerOfActiveShop();
  });

  readonly canManageSales = computed(() => this.hasActiveShop());

  readonly canManageExpenses = computed(() => this.isOwnerOrManagerOfActiveShop());

  readonly canManageDiscounts = computed(() => this.isOwnerOrManagerOfActiveShop());

  readonly canManageServices = computed(() => this.isOwnerOrManagerOfActiveShop());

  readonly canManageBankAccounts = computed(() => this.isOwnerOfActiveShop());

  readonly canViewInventory = computed(() => this.hasActiveShop());
}
