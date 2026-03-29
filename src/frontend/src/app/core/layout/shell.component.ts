import { CommonModule } from '@angular/common';
import { Component, ElementRef, HostListener, ViewChild, computed, inject, signal } from '@angular/core';
import { Store } from '@ngrx/store';
import { RouterOutlet } from '@angular/router';

import { AuthService } from '../auth/auth.service';
import { UserShop } from '../auth/auth.models';
import { RootState } from '../state/app.state';
import { CreateShopOverlayComponent } from '../../features/shops/components/create-shop-overlay.component';
import { ManageShopOverlayComponent } from '../../features/shops/components/manage-shop-overlay.component';
import { ShopsActions } from '../../features/shops/state/shops.actions';
import { selectShopDetailsEntities, selectShops, selectShopsSubmitting } from '../../features/shops/state/shops.selectors';
import { UpdateProfileOverlayComponent } from '../../features/users/components/update-profile-overlay.component';
import { ChangePasswordOverlayComponent } from '../../features/users/components/change-password-overlay.component';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    CreateShopOverlayComponent,
    ManageShopOverlayComponent,
    UpdateProfileOverlayComponent,
    ChangePasswordOverlayComponent,
  ],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
})
export class ShellComponent {
  private readonly authService = inject(AuthService);
  private readonly store = inject(Store<RootState>);

  @ViewChild('shopMenuRoot') shopMenuRoot?: ElementRef<HTMLElement>;
  @ViewChild('profileMenuRoot') profileMenuRoot?: ElementRef<HTMLElement>;

  readonly isSigningOut = signal(false);
  readonly isProfileMenuOpen = signal(false);
  readonly isShopMenuOpen = signal(false);
  readonly showCreateShopOverlayManual = signal(false);
  readonly showManageShopOverlay = signal(false);
  readonly showUpdateProfileOverlay = signal(false);
  readonly showChangePasswordOverlay = signal(false);

  readonly session = this.authService.session;
  readonly shops = this.store.selectSignal(selectShops);
  readonly shopDetailsById = this.store.selectSignal(selectShopDetailsEntities);
  readonly isShopsSubmitting = this.store.selectSignal(selectShopsSubmitting);
  readonly showCreateShopOverlay = computed(() => this.authService.needsShopSetup() || this.showCreateShopOverlayManual());
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

  constructor() {
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

    if (this.isShopMenuOpen() && this.shopMenuRoot && !this.isTargetInside(this.shopMenuRoot.nativeElement, target, composedPath)) {
      this.isShopMenuOpen.set(false);
    }

    if (this.isProfileMenuOpen() && this.profileMenuRoot && !this.isTargetInside(this.profileMenuRoot.nativeElement, target, composedPath)) {
      this.isProfileMenuOpen.set(false);
    }
  }

  private isTargetInside(root: HTMLElement, target: Node, composedPath: readonly EventTarget[]): boolean {
    return root.contains(target) || composedPath.includes(root);
  }

  onToggleProfileMenu(): void {
    this.isShopMenuOpen.set(false);
    this.isProfileMenuOpen.set(!this.isProfileMenuOpen());
  }

  onToggleShopMenu(): void {
    if (this.shops().length === 0) {
      return;
    }

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
    this.showCreateShopOverlayManual.set(false);

    if (!this.authService.needsShopSetup()) {
      return;
    }

    this.onSignOut();
  }
}
