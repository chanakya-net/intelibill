import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';

import { AuthService } from '../auth/auth.service';
import { CreateShopOverlayComponent } from '../../features/shops/components/create-shop-overlay.component';
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
    UpdateProfileOverlayComponent,
    ChangePasswordOverlayComponent,
    SetDefaultStoreOverlayComponent,
  ],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
})
export class ShellComponent {
  private readonly authService = inject(AuthService);

  readonly isSigningOut = signal(false);
  readonly isProfileMenuOpen = signal(false);
  readonly showCreateShopOverlayManual = signal(false);
  readonly showUpdateProfileOverlay = signal(false);
  readonly showChangePasswordOverlay = signal(false);
  readonly showSetDefaultStoreOverlay = signal(false);

  readonly session = this.authService.session;
  readonly showCreateShopOverlay = computed(() => this.authService.needsShopSetup() || this.showCreateShopOverlayManual());
  readonly shouldShowSetDefaultStoreAction = computed(() => {
    const shops = this.session()?.shops ?? [];
    return shops.length > 1;
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

  onProfileOverlayClose(): void {
    this.showUpdateProfileOverlay.set(false);
    this.showChangePasswordOverlay.set(false);
    this.showSetDefaultStoreOverlay.set(false);
  }

  onCreateShopOverlayClose(): void {
    this.showCreateShopOverlayManual.set(false);

    if (!this.authService.needsShopSetup()) {
      return;
    }

    this.onSignOut();
  }
}
