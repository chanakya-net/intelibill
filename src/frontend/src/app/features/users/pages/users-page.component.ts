import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';

import { AuthService } from '../../../core/auth/auth.service';
import { RootState } from '../../../core/state/app.state';
import { AddShopUserOverlayComponent } from '../components/add-shop-user-overlay.component';
import { EditShopUserOverlayComponent } from '../components/edit-shop-user-overlay.component';
import { ShopUser } from '../services/user-account.service';
import { UsersActions } from '../state/users.actions';
import {
  selectShopUsers,
  selectUsersErrorMessage,
  selectUsersLastMutationSucceeded,
  selectUsersLastMutationType,
  selectUsersLoadingShopUsers,
} from '../state/users.selectors';

@Component({
  selector: 'app-users-page',
  standalone: true,
  imports: [CommonModule, ButtonModule, ProgressSpinnerModule, TableModule, AddShopUserOverlayComponent, EditShopUserOverlayComponent, TranslocoPipe],
  templateUrl: './users-page.component.html',
  styleUrl: './users-page.component.scss',
})
export class UsersPageComponent {
  private readonly store = inject(Store<RootState>);
  private readonly authService = inject(AuthService);

  readonly users = this.store.selectSignal(selectShopUsers);
  readonly tableUsers = computed(() => [...this.users()]);
  readonly isLoading = this.store.selectSignal(selectUsersLoadingShopUsers);
  readonly serverError = this.store.selectSignal(selectUsersErrorMessage);
  readonly lastMutationType = this.store.selectSignal(selectUsersLastMutationType);
  readonly lastMutationSucceeded = this.store.selectSignal(selectUsersLastMutationSucceeded);

  readonly showAddUserOverlay = signal(false);
  readonly showEditUserOverlay = signal(false);
  readonly editingUser = signal<ShopUser | null>(null);
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
  readonly canEditUsers = computed(() => this.activeShopRole().toLowerCase() === 'owner');

  constructor() {
    this.store.dispatch(UsersActions.loadShopUsersRequested());

    effect(() => {
      if (!this.lastMutationSucceeded()) {
        return;
      }

      const mutationType = this.lastMutationType();
      if (mutationType === 'add-shop-user' && this.showAddUserOverlay()) {
        this.showAddUserOverlay.set(false);
        this.store.dispatch(UsersActions.clearMutationStatus());
        return;
      }

      if (mutationType === 'edit-shop-user' && this.showEditUserOverlay()) {
        this.showEditUserOverlay.set(false);
        this.editingUser.set(null);
        this.store.dispatch(UsersActions.clearMutationStatus());
      }
    });
  }

  onOpenAddUser(): void {
    this.store.dispatch(UsersActions.clearError());
    this.store.dispatch(UsersActions.clearMutationStatus());
    this.showAddUserOverlay.set(true);
  }

  onCloseAddUser(): void {
    this.showAddUserOverlay.set(false);
  }

  onOpenEditUser(user: ShopUser): void {
    if (!this.canEditUsers() || !this.canEditRole(user.role)) {
      return;
    }

    this.store.dispatch(UsersActions.clearError());
    this.store.dispatch(UsersActions.clearMutationStatus());
    this.editingUser.set(user);
    this.showEditUserOverlay.set(true);
  }

  onCloseEditUser(): void {
    this.showEditUserOverlay.set(false);
    this.editingUser.set(null);
  }

  getRoleLabel(role: string): string {
    const normalized = role.trim().toLowerCase();
    if (normalized === 'salesperson' || normalized === 'staff') {
      return 'users.staff';
    }

    if (normalized === 'manager') {
      return 'users.manager';
    }

    if (normalized === 'owner') {
      return 'users.owner';
    }

    return role;
  }

  canEditRole(role: string): boolean {
    return role.trim().toLowerCase() !== 'owner';
  }
}
