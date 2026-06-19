import { computed, inject, Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { MenuItem } from 'primeng/api';

import { ShopPermissionsService } from './shop-permissions.service';
import { LocalizationService } from '../i18n/localization.service';
import { SupportedLanguage, NATIVE_LANGUAGE_NAMES, SUPPORTED_LANGUAGES } from '../i18n/language.constants';

@Injectable({ providedIn: 'root' })
export class ShellMenuService {
  private readonly permissions = inject(ShopPermissionsService);
  private readonly router = inject(Router);
  private readonly localizationService = inject(LocalizationService);
  private readonly currentLanguage = this.localizationService.currentLanguage;
  readonly panelMenuPt = {
    root: { class: 'shell-panelmenu-root' },
    panel: { class: 'shell-panelmenu-panel' },
    header: { class: 'shell-panelmenu-header' },
    headerContent: { class: 'shell-panelmenu-header-content' },
    headerLink: { class: 'shell-panelmenu-header-link' },
    headerIcon: { class: 'shell-panelmenu-header-icon' },
    headerLabel: { class: 'shell-panelmenu-header-label' },
    contentContainer: { class: 'shell-panelmenu-content-container' },
    content: { class: 'shell-panelmenu-content' },
    item: { class: 'shell-panelmenu-item' },
    itemContent: { class: 'shell-panelmenu-item-content' },
    itemLink: { class: 'shell-panelmenu-item-link' },
    itemIcon: { class: 'shell-panelmenu-item-icon' },
    itemLabel: { class: 'shell-panelmenu-item-label' },
    submenuIcon: { class: 'shell-panelmenu-submenu-icon' },
    submenu: { class: 'shell-panelmenu-submenu' },
  };

  readonly mainMenuItems = computed<MenuItem[]>(() => {
    this.currentLanguage();

    const items: MenuItem[] = [
      {
        label: this.localizationService.translate('shell.dashboard'),
        icon: 'pi pi-home',
        routerLink: ['/dashboard'],
        command: () => this.router.navigate(['/dashboard']),
      },
    ];

    const inventoryItems = this.inventoryMenuItems();
    if (inventoryItems.length > 0) {
      items.push({
        label: this.localizationService.translate('shell.manageInventory'),
        icon: 'pi pi-box',
        items: inventoryItems,
      });
    }

    const supplierItems = this.supplierMenuItems();
    if (supplierItems.length > 0) {
      items.push({
        label: this.localizationService.translate('shell.manageSuppliers'),
        icon: 'pi pi-truck',
        items: supplierItems,
      });
    }

    if (this.permissions.canManageCustomers()) {
      items.push({
        label: this.localizationService.translate('shell.manageCustomers'),
        icon: 'pi pi-address-book',
        routerLink: ['/customers'],
        command: () => this.router.navigate(['/customers']),
      });
    }

    if (this.permissions.canManageSales()) {
      items.push({
        label: this.localizationService.translate('shell.manageSales'),
        icon: 'pi pi-shopping-cart',
        items: this.salesMenuItems(),
      });
    }

    if (this.permissions.canManageExpenses()) {
      items.push({
        label: this.localizationService.translate('shell.manageExpenses'),
        icon: 'pi pi-wallet',
        routerLink: ['/expenses'],
        command: () => this.router.navigate(['/expenses']),
      });
    }

    return items;
  });

  readonly inventoryMenuItems = computed<MenuItem[]>(() => {
    this.currentLanguage();
    if (!this.permissions.canViewInventory()) {
      return [];
    }

    const items: MenuItem[] = [];

    if (this.permissions.isOwnerOrManagerOfActiveShop()) {
      items.push(
        {
          label: this.localizationService.translate('shell.addNewProduct'),
          icon: 'pi pi-plus-circle',
          routerLink: ['/inventory'],
          command: () => this.router.navigate(['/inventory']),
        },
        {
          label: this.localizationService.translate('shell.manageServices'),
          icon: 'pi pi-briefcase',
          routerLink: ['/services'],
          command: () => this.router.navigate(['/services']),
        },
        {
          label: this.localizationService.translate('shell.batchInventoryInbound'),
          icon: 'pi pi-plus',
          routerLink: ['/inventory/batch'],
          command: () => this.router.navigate(['/inventory/batch']),
        },
        {
          label: this.localizationService.translate('shell.inventoryBatchesOverview'),
          icon: 'pi pi-list',
          routerLink: ['/inventory/batches'],
          command: () => this.router.navigate(['/inventory/batches']),
        },
      );
    }

    items.push({
      label: this.localizationService.translate('shell.inventoryAdjustments'),
      icon: 'pi pi-history',
      routerLink: ['/inventory/adjustments'],
      command: () => this.router.navigate(['/inventory/adjustments']),
    });

    return items;
  });

  private supplierMenuItems(): MenuItem[] {
    const items: MenuItem[] = [];

    if (this.permissions.canManageSuppliers()) {
      items.push({
        label: this.localizationService.translate('suppliers.supplierDirectory'),
        icon: 'pi pi-truck',
        routerLink: ['/suppliers'],
        command: () => this.router.navigate(['/suppliers']),
      });
    }

    if (this.permissions.canViewInventory()) {
      items.push({
        label: this.localizationService.translate('shell.purchaseOrders'),
        icon: 'pi pi-list',
        routerLink: ['/inventory/purchase-orders'],
        command: () => this.router.navigate(['/inventory/purchase-orders']),
      });
    }

    return items;
  }

  private salesMenuItems(): MenuItem[] {
    if (!this.permissions.canManageSales()) {
      return [];
    }

    const items: MenuItem[] = [
      {
        label: this.localizationService.translate('shell.newSale'),
        icon: 'pi pi-plus-circle',
        routerLink: ['/sales/new'],
        command: () => this.router.navigate(['/sales/new']),
      },
      {
        label: this.localizationService.translate('shell.salesHistory'),
        icon: 'pi pi-list',
        routerLink: ['/sales'],
        command: () => this.router.navigate(['/sales']),
      },
      {
        label: this.localizationService.translate('shell.creditNotes'),
        icon: 'pi pi-file-edit',
        routerLink: ['/sales/credit-notes'],
        command: () => this.router.navigate(['/sales/credit-notes']),
      },
    ];

    if (this.permissions.isOwnerOrManagerOfActiveShop()) {
      items.push({
        label: this.localizationService.translate('shell.profitLossReport'),
        icon: 'pi pi-chart-line',
        routerLink: ['/sales/profit-loss'],
        command: () => this.router.navigate(['/sales/profit-loss']),
      });
    }

    return items;
  }

  profileMenuItems(actions: {
    onLanguageSelected: (language: SupportedLanguage) => void;
    closeMenus: () => void;
    isSigningOut: boolean;
    navigate: (path: string) => void;
    onSignOut: () => void;
    hasShops: boolean;
    onOpenAddShop: () => void;
    onOpenManageShop: () => void;
    onOpenUpdateProfile: () => void;
    onOpenChangePassword: () => void;
  }): MenuItem[] {
    const currentLanguage = this.localizationService.currentLanguage();
    const items: MenuItem[] = [
      {
        label: this.localizationService.translate('shell.manageUsers'),
        icon: 'pi pi-users',
        command: () => {
          actions.closeMenus();
          actions.navigate('/users');
        },
      },
      { label: this.localizationService.translate('shell.updateProfile'), icon: 'pi pi-user-edit', command: () => actions.onOpenUpdateProfile() },
      { label: this.localizationService.translate('shell.changePassword'), icon: 'pi pi-key', command: () => actions.onOpenChangePassword() },
    ];

    if (this.permissions.isOwnerOfActiveShop()) {
      items.push({
        label: this.localizationService.translate('shell.addShop'),
        icon: 'pi pi-plus-circle',
        command: () => actions.onOpenAddShop(),
      });
    }

    if (this.permissions.isOwnerOfActiveShop() && actions.hasShops) {
      items.push({
        label: this.localizationService.translate('shell.manageShop'),
        icon: 'pi pi-wrench',
        command: () => actions.onOpenManageShop(),
      });
    }

    if (this.permissions.canManageDiscounts()) {
      items.push({
        label: this.localizationService.translate('shell.manageDiscounts'),
        icon: 'pi pi-tag',
        command: () => {
          actions.closeMenus();
          actions.navigate('/discounts');
        },
      });
    }

    if (this.permissions.isOwnerOfActiveShop()) {
      items.push({
        label: this.localizationService.translate('shell.manageBankAccounts'),
        icon: 'pi pi-building-columns',
        command: () => {
          actions.closeMenus();
          actions.navigate('/bank-accounts');
        },
      });
    }

    items.push({
      label: this.localizationService.translate('shell.language'),
      icon: 'pi pi-globe',
      items: SUPPORTED_LANGUAGES.map((language) => ({
        label: NATIVE_LANGUAGE_NAMES[language] ?? language,
        icon: currentLanguage === language ? 'pi pi-check' : '',
        command: () => actions.onLanguageSelected(language),
      })),
    });

    items.push({
      label: this.localizationService.translate('shell.logout'),
      icon: 'pi pi-sign-out',
      disabled: actions.isSigningOut,
      command: () => actions.onSignOut(),
    });

    return items;
  }
}
