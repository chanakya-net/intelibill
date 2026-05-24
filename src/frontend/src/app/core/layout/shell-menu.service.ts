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
  readonly menubarPt = {
    root: { class: 'menubar-root' },
    menu: { class: 'menubar-menu' },
    menuitem: { class: 'menubar-item' },
    itemlink: { class: 'menubar-item-link' },
    itemlabel: { class: 'menubar-item-label' },
    itemicon: { class: 'menubar-item-icon' },
    submenuicon: { class: 'menubar-submenu-icon' },
    submenu: { class: 'menubar-submenu' },
  };

  readonly mainMenuItems = computed<MenuItem[]>(() => {
    this.currentLanguage();

    const items: MenuItem[] = [
      {
        label: this.localizationService.translate('shell.dashboard'),
        icon: 'pi pi-home',
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

    if (this.permissions.canManageSuppliers()) {
      items.push({
        label: this.localizationService.translate('shell.manageSuppliers'),
        icon: 'pi pi-truck',
        command: () => this.router.navigate(['/suppliers']),
      });
    }

    if (this.permissions.canManageCustomers()) {
      items.push({
        label: this.localizationService.translate('shell.manageCustomers'),
        icon: 'pi pi-address-book',
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
          command: () => this.router.navigate(['/inventory']),
        },
        {
          label: this.localizationService.translate('shell.batchInventoryInbound'),
          icon: 'pi pi-plus',
          command: () => this.router.navigate(['/inventory/batch']),
        },
        {
          label: this.localizationService.translate('shell.inventoryBatchesOverview'),
          icon: 'pi pi-list',
          command: () => this.router.navigate(['/inventory/batches']),
        },
      );
    }

    items.push({
      label: this.localizationService.translate('shell.inventoryAdjustments'),
      icon: 'pi pi-history',
      command: () => this.router.navigate(['/inventory/adjustments']),
    });

    return items;
  });

  private salesMenuItems(): MenuItem[] {
    if (!this.permissions.canManageSales()) {
      return [];
    }

    return [
      {
        label: this.localizationService.translate('shell.newSale'),
        icon: 'pi pi-plus-circle',
        command: () => this.router.navigate(['/sales/new']),
      },
      {
        label: this.localizationService.translate('shell.salesHistory'),
        icon: 'pi pi-list',
        command: () => this.router.navigate(['/sales']),
      },
      {
        label: this.localizationService.translate('shell.profitLossReport'),
        icon: 'pi pi-chart-line',
        command: () => this.router.navigate(['/sales/profit-loss']),
      },
    ];
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
