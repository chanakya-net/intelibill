import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { Store } from '@ngrx/store';

import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';

import { AuthService } from '../../../core/auth/auth.service';
import { RootState } from '../../../core/state/app.state';
import { AddShopUserOverlayComponent } from '../components/add-shop-user-overlay.component';
import { UsersActions } from '../state/users.actions';
import { selectShopUsers, selectUsersErrorMessage, selectUsersLoadingShopUsers } from '../state/users.selectors';

@Component({
  selector: 'app-users-page',
  standalone: true,
  imports: [CommonModule, ButtonModule, ProgressSpinnerModule, TableModule, AddShopUserOverlayComponent],
  templateUrl: './users-page.component.html',
  styleUrl: './users-page.component.scss',
})
export class UsersPageComponent {
  private readonly store = inject(Store<RootState>);
  private readonly authService = inject(AuthService);

  readonly users = this.store.selectSignal(selectShopUsers);
  readonly isLoading = this.store.selectSignal(selectUsersLoadingShopUsers);
  readonly serverError = this.store.selectSignal(selectUsersErrorMessage);

  readonly showAddUserOverlay = signal(false);
  readonly session = this.authService.session;
  readonly activeShopRole = computed(() => {
    const session = this.session();
    if (!session) {
      return '';
    }

    const activeShop = session.shops.find((shop) => shop.shopId === session.activeShopId) ?? session.shops.find((shop) => shop.isDefault);
    return activeShop?.role ?? '';
  });
  readonly canAddUsers = computed(() => this.activeShopRole().toLowerCase() === 'owner');

  constructor() {
    this.store.dispatch(UsersActions.loadShopUsersRequested());
  }

  onOpenAddUser(): void {
    this.store.dispatch(UsersActions.clearError());
    this.store.dispatch(UsersActions.clearMutationStatus());
    this.showAddUserOverlay.set(true);
  }

  onCloseAddUser(): void {
    this.showAddUserOverlay.set(false);
    this.store.dispatch(UsersActions.loadShopUsersRequested());
  }

  getRoleLabel(role: string): string {
    const normalized = role.trim().toLowerCase();
    if (normalized === 'salesperson' || normalized === 'staff') {
      return 'Sales Person';
    }

    return role;
  }
}
