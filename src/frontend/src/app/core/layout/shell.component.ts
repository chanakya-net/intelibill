import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { Store } from '@ngrx/store';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';

import { AuthService } from '../auth/auth.service';
import { RootState } from '../state/app.state';
import { CreateShopOverlayComponent } from '../../features/shops/components/create-shop-overlay.component';
import { ManageShopOverlayComponent } from '../../features/shops/components/manage-shop-overlay.component';
import { ShopsActions } from '../../features/shops/state/shops.actions';
import { selectShops } from '../../features/shops/state/shops.selectors';
import { UpdateProfileOverlayComponent } from '../../features/users/components/update-profile-overlay.component';
import { ChangePasswordOverlayComponent } from '../../features/users/components/change-password-overlay.component';
import { SetDefaultStoreOverlayComponent } from '../../features/users/components/set-default-store-overlay.component';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    TagModule,
    ButtonModule,
    CreateShopOverlayComponent,
    ManageShopOverlayComponent,
    UpdateProfileOverlayComponent,
    ChangePasswordOverlayComponent,
    SetDefaultStoreOverlayComponent,
  ],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
})
export class ShellComponent {
  private readonly authService = inject(AuthService);
  private readonly store = inject(Store<RootState>);

  readonly isSigningOut = signal(false);
  readonly isProfileMenuOpen = signal(false);
  readonly showCreateShopOverlayManual = signal(false);
  readonly showManageShopOverlay = signal(false);
  readonly showUpdateProfileOverlay = signal(false);
  readonly showChangePasswordOverlay = signal(false);
  readonly showSetDefaultStoreOverlay = signal(false);

  readonly session = this.authService.session;
  readonly shops = this.store.selectSignal(selectShops);
  readonly showCreateShopOverlay = computed(() => this.authService.needsShopSetup() || this.showCreateShopOverlayManual());
  readonly activeShopId = computed(() => this.shops().find((shop) => shop.isDefault)?.shopId ?? null);
  readonly shouldShowSetDefaultStoreAction = computed(() => {
    const shops = this.shops();
    return shops.length > 1;
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

  onToggleProfileMenu(): void {
    this.isProfileMenuOpen.set(!this.isProfileMenuOpen());
  }

  onOpenUpdateProfile(): void {
    this.isProfileMenuOpen.set(false);
    this.showUpdateProfileOverlay.set(true);
  }

  onOpenChangePassword(): void {
    this.isProfileMenuOpen.set(false);
    this.showChangePasswordOverlay.set(true);
  }

  onOpenSetDefaultStore(): void {
    this.isProfileMenuOpen.set(false);
    this.showSetDefaultStoreOverlay.set(true);
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
    this.showSetDefaultStoreOverlay.set(false);
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
