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
import { MenuItem } from 'primeng/api';
import { TieredMenu, TieredMenuModule } from 'primeng/tieredmenu';
import { MenubarModule } from 'primeng/menubar';
import { UsersActions } from '../../features/users/state/users.actions';

import { ShopPermissionsService } from './shop-permissions.service';
import { MobileNavComponent } from './mobile-nav.component';
import { ShellMenuService } from './shell-menu.service';

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
    MobileNavComponent,
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
  readonly menuService = inject(ShellMenuService);

  @ViewChild('shopMenuRoot') shopMenuRoot?: ElementRef<HTMLElement>;
  @ViewChild('profileMenuRoot') profileMenuRoot?: ElementRef<HTMLElement>;
  @ViewChild('profileMenu') profileMenu?: TieredMenu;

  readonly isSigningOut = signal(false);
  readonly isProfileMenuOpen = signal(false);
  readonly isShopMenuOpen = signal(false);
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
  readonly activeShop = computed(() => this.shops().find((shop) => shop.isDefault) ?? null);
  readonly activeShopId = computed(() => this.activeShop()?.shopId ?? null);
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
  readonly menubarPt = this.menuService.menubarPt;
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

    if (
      this.isProfileMenuOpen() &&
      this.profileMenuRoot &&
      !this.isTargetInside(this.profileMenuRoot.nativeElement, target, composedPath)
    ) {
      this.isProfileMenuOpen.set(false);
      this.profileMenu?.hide();
    }
  }

  private isTargetInside(
    root: HTMLElement,
    target: Node,
    composedPath: readonly EventTarget[],
  ): boolean {
    return root.contains(target) || composedPath.includes(root);
  }

  onToggleProfileMenu(event: MouseEvent): void {
    this.isShopMenuOpen.set(false);
    this.profileMenu?.toggle(event);
    this.isProfileMenuOpen.set(!this.isProfileMenuOpen());
  }

  onToggleShopMenu(): void {
    if (this.shops().length === 0 || !this.shopPermissionsService.isOwnerOfActiveShop()) return;
    this.isProfileMenuOpen.set(false);
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
    this.isProfileMenuOpen.set(false);
    this.profileMenu?.hide();
  }

  onMenuItemSelected(item: MenuItem): void { item.command?.({ originalEvent: new Event('click'), item }); }

  onOpenUpdateProfile(): void { this.isProfileMenuOpen.set(false); this.showUpdateProfileOverlay.set(true); }
  onOpenChangePassword(): void { this.isProfileMenuOpen.set(false); this.showChangePasswordOverlay.set(true); }
  onOpenAddShop(): void { this.isProfileMenuOpen.set(false); this.showCreateShopOverlayManual.set(true); }
  onOpenManageShop(): void { this.isProfileMenuOpen.set(false); this.showManageShopOverlay.set(true); }
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
