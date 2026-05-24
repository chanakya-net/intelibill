import { Injectable, computed, inject } from '@angular/core';

import { AuthService } from '../auth/auth.service';

@Injectable({ providedIn: 'root' })
export class ShopPermissionsService {
  private readonly authService = inject(AuthService);

  private readonly activeShop = computed(() => {
    const session = this.authService.session();
    if (!session) {
      return null;
    }

    return (
      session.shops.find((shop) => shop.shopId === session.activeShopId)
      ?? session.shops.find((shop) => shop.isDefault)
      ?? null
    );
  });

  private readonly activeShopRole = computed(() => this.activeShop()?.role.trim().toLowerCase() ?? '');

  readonly isOwnerOfActiveShop = computed(() => this.activeShopRole() === 'owner');
  readonly isOwnerOrManagerOfActiveShop = computed(() =>
    ['owner', 'manager'].includes(this.activeShopRole()),
  );
  readonly canManageSuppliers = computed(() => this.activeShopRole() === 'owner');
  readonly canManageCustomers = computed(() =>
    ['owner', 'manager'].includes(this.activeShopRole()),
  );
  readonly canManageSales = computed(() =>
    ['owner', 'manager', 'staff'].includes(this.activeShopRole()),
  );
  readonly canManageExpenses = computed(() =>
    ['owner', 'manager'].includes(this.activeShopRole()),
  );
  readonly canManageDiscounts = computed(() =>
    ['owner', 'manager'].includes(this.activeShopRole()),
  );
  readonly canManageBankAccounts = computed(() => this.activeShopRole() === 'owner');
  readonly canViewInventory = computed(() =>
    ['owner', 'manager', 'staff'].includes(this.activeShopRole()),
  );
}
