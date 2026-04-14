import {
  Component,
  ElementRef,
  HostListener,
  ViewChild,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { Store } from '@ngrx/store';
import { Router, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';

import { AuthService } from '../auth/auth.service';
import { UserShop } from '../auth/auth.models';
import { RootState } from '../state/app.state';
import { CreateShopOverlayComponent } from '../../features/shops/components/create-shop-overlay.component';
import { ManageShopOverlayComponent } from '../../features/shops/components/manage-shop-overlay.component';
import { ShopsActions } from '../../features/shops/state/shops.actions';
import {
  selectShopDetailsEntities,
  selectShops,
  selectShopsSubmitting,
} from '../../features/shops/state/shops.selectors';
import { UpdateProfileOverlayComponent } from '../../features/users/components/update-profile-overlay.component';
import { ChangePasswordOverlayComponent } from '../../features/users/components/change-password-overlay.component';
import { LocalizationService } from '../i18n/localization.service';
import {
  DEFAULT_LANGUAGE,
  NATIVE_LANGUAGE_NAMES,
  SUPPORTED_LANGUAGES,
  SupportedLanguage,
} from '../i18n/language.constants';
import { MenuItem } from 'primeng/api';
import { TieredMenu, TieredMenuModule } from 'primeng/tieredmenu';
import { MenubarModule } from 'primeng/menubar';
import { UsersActions } from '../../features/users/state/users.actions';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    RouterOutlet,
    CreateShopOverlayComponent,
    ManageShopOverlayComponent,
    UpdateProfileOverlayComponent,
    ChangePasswordOverlayComponent,
    TieredMenuModule,
    MenubarModule,
    TranslocoPipe,
  ],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
})
export class ShellComponent {
  readonly mainMenuItems = computed<MenuItem[]>(() => {
    // Track language changes to re-evaluate menu labels
    this.currentLanguage();

    const items: MenuItem[] = [];
    if (this.isOwnerOrManagerOfActiveShop()) {
      items.push({
        label: this.localizationService.translate('shell.manageInventory'),
        icon: 'pi pi-box',
        items: [
          {
            label: this.localizationService.translate('shell.addNewProduct'),
            icon: 'pi pi-plus-circle',
            command: () => this.onOpenAddProduct(),
          },
          {
            label: this.localizationService.translate('shell.batchInventoryInbound'),
            icon: 'pi pi-plus',
            command: () => this.onOpenInventoryBatch(),
            },
            {
            label: this.localizationService.translate('shell.inventoryBatchesOverview'),
            icon: 'pi pi-list',
            command: () => this.onOpenInventoryBatchesOverview(),
            },
        ],
      });
    }
    if (this.canManageSuppliers()) {
      items.push({
        label: this.localizationService.translate('shell.manageSuppliers'),
        icon: 'pi pi-truck',
        command: () => this.onNavigateToSuppliers(),
      });
    }
    if (this.canManageSales()) {
      items.push({
        label: this.localizationService.translate('shell.manageSales'),
        icon: 'pi pi-shopping-cart',
        items: [
          {
            label: this.localizationService.translate('shell.newSale'),
            icon: 'pi pi-plus-circle',
            command: () => this.onOpenNewSale(),
          },
          {
            label: this.localizationService.translate('shell.salesHistory'),
            icon: 'pi pi-list',
            command: () => this.onOpenSalesHistory(),
          },
        ],
      });
    }
    // Add more global menu items as needed
    return items;
  });

  private readonly authService = inject(AuthService);
  private readonly store = inject(Store<RootState>);
  private readonly router = inject(Router);
  private readonly localizationService = inject(LocalizationService);

  @ViewChild('shopMenuRoot') shopMenuRoot?: ElementRef<HTMLElement>;
  @ViewChild('inventoryMenuRoot') inventoryMenuRoot?: ElementRef<HTMLElement>;
  @ViewChild('inventoryMenu') inventoryMenu?: TieredMenu;
  @ViewChild('profileMenuRoot') profileMenuRoot?: ElementRef<HTMLElement>;
  @ViewChild('profileMenu') profileMenu?: TieredMenu;
  @ViewChild('mobileNavRef') mobileNavRef?: ElementRef<HTMLElement>;
  @ViewChild('mobileMenuTrigger') mobileMenuTrigger?: ElementRef<HTMLElement>;

  readonly isSigningOut = signal(false);
  readonly isProfileMenuOpen = signal(false);
  readonly isInventoryMenuOpen = signal(false);
  readonly isShopMenuOpen = signal(false);
  readonly isMobileMenuOpen = signal(false);
  readonly expandedMobileSectionLabel = signal<string | null>(null);
  readonly expandedMobileSections = signal<Set<string>>(new Set(['inventory', 'profile', 'sales']));
  readonly showCreateShopOverlayManual = signal(false);
  readonly showCreateShopOverlayAuto = signal(false);
  readonly showManageShopOverlay = signal(false);
  readonly showUpdateProfileOverlay = signal(false);
  readonly showChangePasswordOverlay = signal(false);
  readonly currentLanguage = this.localizationService.currentLanguage;
  readonly supportedLanguages = SUPPORTED_LANGUAGES;

  readonly session = this.authService.session;
  readonly shops = this.store.selectSignal(selectShops);
  readonly shopDetailsById = this.store.selectSignal(selectShopDetailsEntities);
  readonly isShopsSubmitting = this.store.selectSignal(selectShopsSubmitting);
  readonly showCreateShopOverlay = computed(
    () => this.showCreateShopOverlayAuto() || this.showCreateShopOverlayManual(),
  );
  readonly activeShop = computed(() => this.shops().find((shop) => shop.isDefault) ?? null);
  readonly activeShopId = computed(() => this.activeShop()?.shopId ?? null);
  readonly activeShopLabel = computed(() => {
    const activeShop = this.activeShop();
    if (!activeShop) {
      return null;
    }

    return this.getShopDisplayLabel(activeShop);
  });
  readonly shouldShowManageShopAction = computed(() => {
    const shops = this.shops();
    return shops.length > 0;
  });
  readonly isOwnerOfActiveShop = computed(() => {
    const activeShop = this.activeShop();
    if (!activeShop) {
      return false;
    }

    return activeShop.role.toLowerCase() === 'owner';
  });
  readonly isOwnerOrManagerOfActiveShop = computed(() => {
    const activeShop = this.activeShop();
    if (!activeShop) {
      return false;
    }

    const role = activeShop.role.toLowerCase();
    return role === 'owner' || role === 'manager';
  });
  readonly canManageSuppliers = computed(() => {
    const activeShop = this.activeShop();
    if (!activeShop) {
      return false;
    }

    return activeShop.role.toLowerCase() === 'owner';
  });
  readonly canManageSales = computed(() => {
    const activeShop = this.activeShop();
    if (!activeShop) {
      return false;
    }

    // All roles (Owner, Manager, Staff) can manage sales
    const role = activeShop.role.toLowerCase();
    return role === 'owner' || role === 'manager' || role === 'staff';
  });
  readonly profileInitials = computed(() => {
    const user = this.session()?.user;
    if (!user) {
      return 'U';
    }

    const first = user.firstName?.trim().charAt(0).toUpperCase() ?? '';
    const last = user.lastName?.trim().charAt(0).toUpperCase() ?? '';
    const initials = `${first}${last}`.trim();
    return initials || 'U';
  });
  readonly profileDisplayName = computed(() => {
    const user = this.session()?.user;
    if (!user) {
      return this.localizationService.translate('shell.userFallback');
    }

    const firstName = user.firstName?.trim() ?? '';
    return firstName || user.email || this.localizationService.translate('shell.userFallback');
  });
  readonly profileMenuItems = computed<MenuItem[]>(() => {
    const currentLanguage = this.currentLanguage();
    const items: MenuItem[] = [
      {
        label: this.localizationService.translate('shell.manageUsers'),
        icon: 'pi pi-users',
        command: () => {
          this.onCloseMenus();
          void this.router.navigate(['/users']);
        },
      },
      {
        label: this.localizationService.translate('shell.updateProfile'),
        icon: 'pi pi-user-edit',
        command: () => this.onOpenUpdateProfile(),
      },
      {
        label: this.localizationService.translate('shell.changePassword'),
        icon: 'pi pi-key',
        command: () => this.onOpenChangePassword(),
      },
    ];

    if (this.isOwnerOfActiveShop()) {
      items.push({
        label: this.localizationService.translate('shell.addShop'),
        icon: 'pi pi-plus-circle',
        command: () => this.onOpenAddShop(),
      });
    }

    if (this.shouldShowManageShopAction() && this.isOwnerOfActiveShop()) {
      items.push({
        label: this.localizationService.translate('shell.manageShop'),
        icon: 'pi pi-wrench',
        command: () => this.onOpenManageShop(),
      });
    }

    items.push({
      label: this.localizationService.translate('shell.language'),
      icon: 'pi pi-globe',
      items: this.supportedLanguages.map((language) => ({
        label: NATIVE_LANGUAGE_NAMES[language] ?? language,
        icon: currentLanguage === language ? 'pi pi-check' : '',
        command: () => this.onLanguageSelected(language),
      })),
    });

    items.push({
      label: this.localizationService.translate('shell.logout'),
      icon: 'pi pi-sign-out',
      disabled: this.isSigningOut(),
      command: () => this.onSignOut(),
    });

    return items;
  });
  readonly inventoryMenuItems = computed<MenuItem[]>(() => {
    // Track language changes to re-evaluate menu labels
    this.currentLanguage();

    if (!this.isOwnerOrManagerOfActiveShop()) {
      return [];
    }

    return [
      {
        label: this.localizationService.translate('shell.addNewProduct'),
        icon: 'pi pi-plus-circle',
        command: () => this.onOpenAddProduct(),
      },
      {
        label: this.localizationService.translate('shell.batchInventoryInbound'),
        icon: 'pi pi-plus',
        command: () => this.onOpenInventoryBatch(),
        },
        {
        label: this.localizationService.translate('shell.inventoryBatchesOverview'),
        icon: 'pi pi-list',
        command: () => this.onOpenInventoryBatchesOverview(),
        },
    ];
  });
  readonly salesMenuItems = computed<MenuItem[]>(() => {
    // Track language changes to re-evaluate menu labels
    this.currentLanguage();

    if (!this.canManageSales()) {
      return [];
    }

    return [
      {
        label: this.localizationService.translate('shell.newSale'),
        icon: 'pi pi-plus-circle',
        command: () => this.onOpenNewSale(),
      },
      {
        label: this.localizationService.translate('shell.salesHistory'),
        icon: 'pi pi-list',
        command: () => this.onOpenSalesHistory(),
      },
    ];
  });

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

  constructor() {
    effect(() => {
      if (this.authService.needsShopSetup()) {
        this.showCreateShopOverlayAuto.set(true);
      }
    });

    this.store.dispatch(ShopsActions.loadShopsRequested());
  }

  onSignOut(): void {
    if (this.isSigningOut()) {
      return;
    }

    this.isSigningOut.set(true);
    this.authService.signOutAndRedirect().subscribe({
      complete: () => {
        this.isSigningOut.set(false);
      },
    });
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    this.closeMenusForEvent(event);
  }

  @HostListener('document:pointerdown', ['$event'])
  onDocumentPointerDown(event: PointerEvent): void {
    this.closeMenusForEvent(event);
  }

  private closeMenusForEvent(event: MouseEvent | PointerEvent): void {
    const target = event.target as Node | null;
    if (!target) {
      return;
    }

    const composedPath = event.composedPath?.() ?? [];

    if (
      this.isShopMenuOpen() &&
      this.shopMenuRoot &&
      !this.isTargetInside(this.shopMenuRoot.nativeElement, target, composedPath)
    ) {
      this.isShopMenuOpen.set(false);
    }

    if (
      this.isInventoryMenuOpen() &&
      this.inventoryMenuRoot &&
      !this.isTargetInside(this.inventoryMenuRoot.nativeElement, target, composedPath)
    ) {
      this.isInventoryMenuOpen.set(false);
      this.inventoryMenu?.hide();
    }

    if (
      this.isProfileMenuOpen() &&
      this.profileMenuRoot &&
      !this.isTargetInside(this.profileMenuRoot.nativeElement, target, composedPath)
    ) {
      this.isProfileMenuOpen.set(false);
      this.profileMenu?.hide();
    }

    if (this.isMobileMenuOpen()) {
      const isInNav = this.mobileNavRef?.nativeElement
        ? this.isTargetInside(this.mobileNavRef.nativeElement, target, composedPath)
        : false;
      const isInTrigger = this.mobileMenuTrigger?.nativeElement
        ? this.isTargetInside(this.mobileMenuTrigger.nativeElement, target, composedPath)
        : false;
      if (!isInNav && !isInTrigger) {
        this.closeMobileMenu();
      }
    }
  }

  private isTargetInside(
    root: HTMLElement,
    target: Node,
    composedPath: readonly EventTarget[],
  ): boolean {
    return root.contains(target) || composedPath.includes(root);
  }

  onToggleProfileMenu(event?: MouseEvent, menu?: TieredMenu): void {
    this.isShopMenuOpen.set(false);
    this.isInventoryMenuOpen.set(false);
    this.inventoryMenu?.hide();

    if (event && menu) {
      menu.toggle(event);
      return;
    }

    this.isProfileMenuOpen.set(!this.isProfileMenuOpen());
  }

  onToggleShopMenu(): void {
    if (this.shops().length === 0 || !this.isOwnerOfActiveShop()) {
      return;
    }

    this.isProfileMenuOpen.set(false);
    this.isInventoryMenuOpen.set(false);
    this.inventoryMenu?.hide();
    this.isShopMenuOpen.set(!this.isShopMenuOpen());
  }

  onToggleInventoryMenu(event?: MouseEvent, menu?: TieredMenu): void {
    if (!this.isOwnerOrManagerOfActiveShop()) {
      return;
    }

    this.isShopMenuOpen.set(false);
    this.isProfileMenuOpen.set(false);
    this.profileMenu?.hide();

    if (event && menu) {
      menu.toggle(event);
      return;
    }

    this.isInventoryMenuOpen.set(!this.isInventoryMenuOpen());
  }

  onSelectShop(shopId: string): void {
    if (this.isShopsSubmitting()) {
      return;
    }

    if (shopId === this.activeShopId()) {
      this.isShopMenuOpen.set(false);
      return;
    }

    this.store.dispatch(ShopsActions.clearError());
    this.store.dispatch(ShopsActions.clearMutationStatus());
    this.store.dispatch(ShopsActions.setDefaultShopRequested({ shopId }));
    this.isShopMenuOpen.set(false);
  }

  onCloseMenus(): void {
    this.isShopMenuOpen.set(false);
    this.isInventoryMenuOpen.set(false);
    this.isProfileMenuOpen.set(false);
    this.inventoryMenu?.hide();
    this.profileMenu?.hide();
    this.closeMobileMenu();
  }

  closeMobileMenu(): void {
    this.isMobileMenuOpen.set(false);
    this.expandedMobileSectionLabel.set(null);
  }

  isMobileSectionExpanded(key: string): boolean {
    return this.expandedMobileSections().has(key);
  }

  onToggleMobileSection(key: string): void {
    this.expandedMobileSections.update((sections) => {
      const next = new Set(sections);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }
      return next;
    });
  }

  onToggleMobileMenu(): void {
    this.isShopMenuOpen.set(false);
    this.isInventoryMenuOpen.set(false);
    this.isProfileMenuOpen.set(false);
    this.inventoryMenu?.hide();
    this.profileMenu?.hide();
    this.isMobileMenuOpen.update((open) => !open);
    if (!this.isMobileMenuOpen()) {
      this.expandedMobileSectionLabel.set(null);
    }
  }

  onMobileNavItemClick(item: MenuItem): void {
    if (item.disabled) {
      return;
    }
    if (item.items?.length) {
      this.expandedMobileSectionLabel.update((current) =>
        current === item.label ? null : (item.label ?? null),
      );
      return;
    }
    item.command?.({ originalEvent: new Event('click'), item });
    this.closeMobileMenu();
  }

  getShopDisplayLabel(shop: UserShop): string {
    const pincode = this.shopDetailsById()[shop.shopId]?.pincode?.trim();
    return pincode ? `${shop.shopName} - ${pincode}` : shop.shopName;
  }

  onOpenUpdateProfile(): void {
    this.isProfileMenuOpen.set(false);
    this.showUpdateProfileOverlay.set(true);
  }

  onOpenChangePassword(): void {
    this.isProfileMenuOpen.set(false);
    this.showChangePasswordOverlay.set(true);
  }

  onOpenAddShop(): void {
    this.isProfileMenuOpen.set(false);
    this.showCreateShopOverlayManual.set(true);
  }

  onOpenAddProduct(): void {
    this.onCloseMenus();
    void this.router.navigate(['/inventory']);
  }

  onOpenInventoryBatch(): void {
    this.onCloseMenus();
    void this.router.navigate(['/inventory/batch']);
  }

  onOpenInventoryBatchesOverview(): void {
    this.onCloseMenus();
    void this.router.navigate(['/inventory/batches']);
  }

  onNavigateToSuppliers(): void {
    this.onCloseMenus();
    void this.router.navigate(['/suppliers']);
  }

  onOpenNewSale(): void {
    this.onCloseMenus();
    void this.router.navigate(['/sales/new']);
  }

  onOpenSalesHistory(): void {
    this.onCloseMenus();
    void this.router.navigate(['/sales/history']);
  }

  onOpenManageShop(): void {
    this.isProfileMenuOpen.set(false);
    this.showManageShopOverlay.set(true);
  }

  onProfileOverlayClose(): void {
    this.showUpdateProfileOverlay.set(false);
    this.showChangePasswordOverlay.set(false);
  }

  onManageShopOverlayClose(): void {
    this.showManageShopOverlay.set(false);
  }

  onCreateShopOverlayClose(): void {
    this.showCreateShopOverlayAuto.set(false);
    this.showCreateShopOverlayManual.set(false);

    if (!this.authService.needsShopSetup()) {
      return;
    }

    this.onSignOut();
  }

  onLanguageSelected(language: SupportedLanguage): void {
    const currentLanguage = this.currentLanguage();
    if (currentLanguage === language) {
      return;
    }

    void this.localizationService.setLanguage(language);

    const user = this.session()?.user;
    if (!user) {
      return;
    }

    this.store.dispatch(UsersActions.clearError());
    this.store.dispatch(
      UsersActions.updateProfileRequested({
        payload: {
          email: user.email ?? '',
          phoneNumber: user.phoneNumber,
          firstName: user.firstName,
          lastName: user.lastName,
          language: language || user.language || DEFAULT_LANGUAGE,
        },
      }),
    );
  }
}
