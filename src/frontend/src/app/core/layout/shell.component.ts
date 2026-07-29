import { Component, ElementRef, HostListener, ViewChild, computed, effect, inject, signal } from '@angular/core';
import { Store } from '@ngrx/store';
import { Router, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';

import { AuthService } from '../auth/auth.service';
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
import { DEFAULT_LANGUAGE, SupportedLanguage } from '../i18n/language.constants';
import { UsersActions } from '../../features/users/state/users.actions';

import { ShopPermissionsService } from './shop-permissions.service';
import { MobileNavComponent } from './mobile-nav.component';
import { SidebarNavComponent } from './sidebar-nav.component';
import { SidebarProfileMenuComponent } from './sidebar-profile-menu.component';
import { ShellMenuService } from './shell-menu.service';
import { AppShellActions } from '../state/app-shell.actions';
import { selectSidebarCollapsed, selectSidebarPinned } from '../state/app-shell.selectors';
import { NetworkStatusService } from '../services/network-status.service';
import {
  OfflineSalesDeviceSettings,
  OfflineSalesDeviceSettingsStorage,
} from '../storage/offline-sales-device-settings.storage';
import { OfflineSalesDeviceEnablementService } from '../../features/sales/services/offline-sales-device-enablement.service';
import { offlineEnablementErrorKeyForReason } from '../../features/shops/components/manage-shop/manage-shop-offline.helper';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    RouterOutlet,
    CreateShopOverlayComponent,
    ManageShopOverlayComponent,
    UpdateProfileOverlayComponent,
    ChangePasswordOverlayComponent,
    MobileNavComponent,
    SidebarNavComponent,
    SidebarProfileMenuComponent,
    TranslocoPipe,
  ],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
})
export class ShellComponent {
  private readonly authService = inject(AuthService);
  private readonly store = inject(Store<RootState>);
  private readonly router = inject(Router);
  private readonly localizationService = inject(LocalizationService);
  private readonly shopPermissionsService = inject(ShopPermissionsService);
  private readonly networkStatus = inject(NetworkStatusService);
  private readonly offlineDeviceSettingsStorage = inject(OfflineSalesDeviceSettingsStorage);
  private readonly offlineDeviceEnablement = inject(OfflineSalesDeviceEnablementService);
  readonly menuService = inject(ShellMenuService);

  @ViewChild('shopMenuRoot') shopMenuRoot?: ElementRef<HTMLElement>;

  readonly isSigningOut = signal(false);
  readonly isShopMenuOpen = signal(false);
  readonly offlineDeviceSettings = signal<OfflineSalesDeviceSettings | null>(null);
  readonly isOfflineSalesEnablePending = signal(false);
  readonly offlineSalesToggleErrorKey = signal('');
  readonly showCreateShopOverlayManual = signal(false);
  readonly showCreateShopOverlayAuto = signal(false);
  readonly showManageShopOverlay = signal(false);
  readonly showUpdateProfileOverlay = signal(false);
  readonly showChangePasswordOverlay = signal(false);
  readonly currentLanguage = this.localizationService.currentLanguage;

  readonly session = this.authService.session;
  readonly shops = this.store.selectSignal(selectShops);
  readonly shopDetailsById = this.store.selectSignal(selectShopDetailsEntities);
  readonly isShopsSubmitting = this.store.selectSignal(selectShopsSubmitting);
  readonly activeShop = this.shopPermissionsService.activeShop;
  readonly activeShopId = this.shopPermissionsService.activeShopId;
  readonly canManageOfflineSales = computed(() => this.shopPermissionsService.isOwnerOrManagerOfActiveShop());
  readonly isOfflineSalesEnabled = computed(() => !!this.offlineDeviceSettings()?.enabled);
  readonly activeShopLabel = computed(() => {
    const activeShop = this.activeShop();
    if (!activeShop) {
      return null;
    }

    const pincode = this.shopDetailsById()[activeShop.shopId]?.pincode?.trim();
    return pincode ? `${activeShop.shopName} - ${pincode}` : activeShop.shopName;
  });
  readonly profileMenuItems = computed(() =>
    this.menuService.profileMenuItems({
      onLanguageSelected: (language) => this.onLanguageSelected(language),
      closeMenus: () => this.onCloseMenus(),
      isSigningOut: this.isSigningOut(),
      navigate: (path) => {
        this.onCloseMenus();
        void this.router.navigate([path]);
      },
      onSignOut: () => this.onSignOut(),
      hasShops: this.shops().length > 0,
      onOpenAddShop: () => this.onOpenAddShop(),
      onOpenManageShop: () => this.onOpenManageShop(),
      onOpenUpdateProfile: () => this.onOpenUpdateProfile(),
      onOpenChangePassword: () => this.onOpenChangePassword(),
    }),
  );
  readonly sidebarCollapsed = this.store.selectSignal(selectSidebarCollapsed);
  readonly sidebarPinned = this.store.selectSignal(selectSidebarPinned);
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

  constructor() {
    effect(() => {
      if (this.authService.needsShopSetup()) {
        this.showCreateShopOverlayAuto.set(true);
      }
    });

    effect(() => {
      const shopId = this.activeShopId();
      this.offlineDeviceSettings.set(shopId ? this.offlineDeviceSettingsStorage.loadSettings(shopId) : null);
      this.offlineSalesToggleErrorKey.set('');
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

  @HostListener('document:keydown', ['$event'])
  onGlobalKeyDown(event: KeyboardEvent): void {
    if (!(event.ctrlKey || event.metaKey) || event.key !== 'k') return;
    event.preventDefault();
    const searchInput = document.querySelector('input[pInputText], input.p-inputtext') as HTMLInputElement;
    if (searchInput) searchInput.focus();
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

  }

  private isTargetInside(
    root: HTMLElement,
    target: Node,
    composedPath: readonly EventTarget[],
  ): boolean {
    return root.contains(target) || composedPath.includes(root);
  }

  async onToggleOfflineSales(): Promise<void> {
    if (this.isOfflineSalesEnablePending()) {
      return;
    }

    const shopId = this.activeShopId();
    if (!shopId || !this.canManageOfflineSales()) {
      return;
    }

    if (this.isOfflineSalesEnabled()) {
      const next = this.offlineDeviceSettingsStorage.updateSettings(shopId, (current) => ({
        ...current,
        enabled: false,
      }));
      this.offlineDeviceSettings.set(next);
      this.offlineSalesToggleErrorKey.set('');
      return;
    }

    await this.networkStatus.checkConnectivity();
    if (!this.networkStatus.canReachApi()) {
      this.offlineSalesToggleErrorKey.set('offlineSalesDevice.errors.apiUnreachable');
      return;
    }

    this.isOfflineSalesEnablePending.set(true);
    this.offlineSalesToggleErrorKey.set('');
    try {
      const label = this.offlineDeviceSettings()?.label?.trim() ?? '';
      const result = await this.offlineDeviceEnablement.enableForShop(shopId, label);
      if (!result.ok) {
        this.offlineSalesToggleErrorKey.set(offlineEnablementErrorKeyForReason(result.reason));
        return;
      }

      this.offlineDeviceSettings.set(result.settings);
    } finally {
      this.isOfflineSalesEnablePending.set(false);
    }
  }

  onToggleShopMenu(): void {
    if (this.shops().length === 0) return;
    this.isShopMenuOpen.set(!this.isShopMenuOpen());
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
  }

  onToggleSidebar(): void {
    this.store.dispatch(AppShellActions.toggleSidebar());
  }

  onExpandSidebar(): void {
    if (this.sidebarCollapsed()) {
      this.store.dispatch(AppShellActions.setSidebarCollapsed({ collapsed: false }));
    }
  }

  onToggleSidebarPin(): void {
    this.store.dispatch(AppShellActions.toggleSidebarPinned());
  }

  onAutoHideSidebar(): void {
    if (this.sidebarPinned() || this.sidebarCollapsed()) {
      return;
    }

    this.store.dispatch(AppShellActions.setSidebarCollapsed({ collapsed: true }));
  }

  onOpenUpdateProfile(): void { this.showUpdateProfileOverlay.set(true); }
  onOpenChangePassword(): void { this.showChangePasswordOverlay.set(true); }
  onOpenAddShop(): void { this.showCreateShopOverlayManual.set(true); }
  onOpenManageShop(): void { this.showManageShopOverlay.set(true); }
  onProfileOverlayClose(): void { this.showUpdateProfileOverlay.set(false); this.showChangePasswordOverlay.set(false); }
  onManageShopOverlayClose(): void { this.showManageShopOverlay.set(false); }

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
