import { Injectable, computed, inject } from '@angular/core';
import { Router } from '@angular/router';

import { MenuItem } from 'primeng/api';

import { LocalizationService } from '../i18n/localization.service';
import { ShopPermissionsService } from './shop-permissions.service';

@Injectable({ providedIn: 'root' })
export class ShellMenuService {
  private readonly router = inject(Router);
  private readonly localizationService = inject(LocalizationService);
  private readonly shopPermissions = inject(ShopPermissionsService);

  readonly inventoryMenuItems = computed<MenuItem[]>(() => {
    this.localizationService.currentLanguage();

    if (!this.shopPermissions.canViewInventory()) {
      return [];
    }

    const items: MenuItem[] = [];

    if (this.shopPermissions.isOwnerOrManagerOfActiveShop()) {
      items.push(
        this.buildNavigationItem('shell.addNewProduct', 'pi pi-plus-circle', ['/inventory']),
        this.buildNavigationItem('shell.batchInventoryInbound', 'pi pi-plus', ['/inventory/batch']),
        this.buildNavigationItem(
          'shell.inventoryBatchesOverview',
          'pi pi-list',
          ['/inventory/batches'],
        ),
      );
    }

    items.push(
      this.buildNavigationItem(
        'shell.inventoryAdjustments',
        'pi pi-history',
        ['/inventory/adjustments'],
      ),
    );

    return items;
  });

  readonly mainMenuItems = computed<MenuItem[]>(() => {
    this.localizationService.currentLanguage();

    const items: MenuItem[] = [
      this.buildNavigationItem('shell.dashboard', 'pi pi-home', ['/dashboard']),
    ];

    const inventoryItems = this.inventoryMenuItems();
    if (inventoryItems.length > 0) {
      items.push({
        label: this.localizationService.translate('shell.manageInventory'),
        icon: 'pi pi-box',
        items: inventoryItems,
      });
    }

    if (this.shopPermissions.canManageSuppliers()) {
      items.push(
        this.buildNavigationItem('shell.manageSuppliers', 'pi pi-truck', ['/suppliers']),
      );
    }

    if (this.shopPermissions.canManageCustomers()) {
      items.push(
        this.buildNavigationItem('shell.manageCustomers', 'pi pi-address-book', ['/customers']),
      );
    }

    if (this.shopPermissions.canManageSales()) {
      items.push({
        label: this.localizationService.translate('shell.manageSales'),
        icon: 'pi pi-shopping-cart',
        items: [
          this.buildNavigationItem('shell.newSale', 'pi pi-plus-circle', ['/sales/new']),
          this.buildNavigationItem('shell.salesHistory', 'pi pi-list', ['/sales']),
          this.buildNavigationItem(
            'shell.profitLossReport',
            'pi pi-chart-line',
            ['/sales/profit-loss'],
          ),
        ],
      });
    }

    if (this.shopPermissions.canManageExpenses()) {
      items.push(
        this.buildNavigationItem('shell.manageExpenses', 'pi pi-wallet', ['/expenses']),
      );
    }

    return items;
  });

  private buildNavigationItem(labelKey: string, icon: string, commands: string[]): MenuItem {
    return {
      label: this.localizationService.translate(labelKey),
      icon,
      command: () => {
        void this.router.navigate(commands);
      },
    };
  }
}
