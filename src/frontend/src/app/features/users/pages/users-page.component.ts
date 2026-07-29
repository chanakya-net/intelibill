import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { AvatarModule } from 'primeng/avatar';
import { SkeletonModule } from 'primeng/skeleton';
import { TableModule } from 'primeng/table';

import { AuthService } from '../../../core/auth/auth.service';
import { RootState } from '../../../core/state/app.state';
import { AddShopUserOverlayComponent } from '../components/add-shop-user-overlay.component';
import { EditShopUserOverlayComponent } from '../components/edit-shop-user-overlay.component';
import { SetDefaultStoreOverlayComponent } from '../components/set-default-store-overlay.component';
import { UserRoleFilter, UsersFilterBarComponent } from '../components/users-filter-bar.component';
import { ShopUser } from '../services/user-account.service';
import { UsersActions } from '../state/users.actions';
import {
  selectShopUsers,
  selectUsersErrorMessage,
  selectUsersLastMutationSucceeded,
  selectUsersLastMutationType,
  selectUsersLoadingShopUsers,
} from '../state/users.selectors';

type UserSummaryCard = {
  labelKey: string;
  value: number;
  tone: 'amber' | 'sage' | 'terracotta' | 'ink';
};

type UserDirectoryMetric = {
  labelKey: string;
  value: number;
};

@Component({
  selector: 'app-users-page',
  standalone: true,
  imports: [
    CommonModule,
    ButtonModule,
    AvatarModule,
    SkeletonModule,
    TableModule,
    AddShopUserOverlayComponent,
    EditShopUserOverlayComponent,
    SetDefaultStoreOverlayComponent,
    UsersFilterBarComponent,
    TranslocoPipe,
  ],
  templateUrl: './users-page.component.html',
  styleUrl: './users-page.component.scss',
})
export class UsersPageComponent {
  private readonly store = inject(Store<RootState>);
  private readonly authService = inject(AuthService);

  readonly users = this.store.selectSignal(selectShopUsers);
  readonly searchValue = signal('');
  readonly roleFilter = signal<UserRoleFilter>('all');
  readonly filteredUsers = computed(() => {
    const q = this.searchValue().toLowerCase().trim();
    const roleFilter = this.roleFilter();

    return this.users().filter((user) => {
      if (roleFilter !== 'all' && this.normalizeRole(user.role) !== roleFilter) {
        return false;
      }

      if (!q) {
        return true;
      }

      return (
        `${user.firstName} ${user.lastName}`.toLowerCase().includes(q) ||
        (user.phoneNumber ?? '').toLowerCase().includes(q) ||
        (user.email ?? '').toLowerCase().includes(q)
      );
    });
  });
  readonly tableUsers = computed(() => [...this.filteredUsers()]);
  readonly isLoading = this.store.selectSignal(selectUsersLoadingShopUsers);
  readonly serverError = this.store.selectSignal(selectUsersErrorMessage);
  readonly lastMutationType = this.store.selectSignal(selectUsersLastMutationType);
  readonly lastMutationSucceeded = this.store.selectSignal(selectUsersLastMutationSucceeded);

  readonly showAddUserOverlay = signal(false);
  readonly showEditUserOverlay = signal(false);
  readonly showDefaultStoreOverlay = signal(false);
  readonly editingUser = signal<ShopUser | null>(null);
  readonly session = this.authService.session;
  readonly activeShopRole = computed(() => {
    const session = this.session();
    if (!session) {
      return '';
    }

    const activeShop =
      session.shops.find((shop) => shop.shopId === session.activeShopId) ??
      session.shops.find((shop) => shop.isDefault);
    return activeShop?.role ?? '';
  });
  readonly canAddUsers = computed(() => this.activeShopRole().toLowerCase() === 'owner');
  readonly canEditUsers = computed(() => this.activeShopRole().toLowerCase() === 'owner');
  readonly totalUsers = computed(() => this.users().length);
  readonly managerCount = computed(
    () => this.users().filter((user) => user.role.trim().toLowerCase() === 'manager').length,
  );
  readonly staffCount = computed(() =>
    this.users().filter((user) => {
      const role = user.role.trim().toLowerCase();
      return role === 'staff' || role === 'salesperson';
    }).length,
  );
  readonly loginEnabledCount = computed(
    () => this.users().filter((user) => user.isLoginEnabled).length,
  );
  readonly summaryCards = computed<UserSummaryCard[]>(() => [
    {
      labelKey: 'users.summary.totalUsers',
      value: this.totalUsers(),
      tone: 'amber',
    },
    {
      labelKey: 'users.summary.managers',
      value: this.managerCount(),
      tone: 'sage',
    },
    {
      labelKey: 'users.summary.staff',
      value: this.staffCount(),
      tone: 'terracotta',
    },
    {
      labelKey: 'users.summary.loginEnabled',
      value: this.loginEnabledCount(),
      tone: 'ink',
    },
  ]);
  readonly directoryMetrics = computed<UserDirectoryMetric[]>(() => [
    {
      labelKey: 'users.summary.totalUsers',
      value: this.totalUsers(),
    },
    {
      labelKey: 'users.summary.managers',
      value: this.managerCount(),
    },
    {
      labelKey: 'users.summary.loginEnabled',
      value: this.loginEnabledCount(),
    },
  ]);

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

  onOpenDefaultStore(): void {
    this.showDefaultStoreOverlay.set(true);
  }

  onCloseDefaultStore(): void {
    this.showDefaultStoreOverlay.set(false);
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

  userInitials(firstName: string, lastName: string): string {
    const f = (firstName ?? '').trim();
    const l = (lastName ?? '').trim();
    if (f && l) return (f[0] + l[0]).toUpperCase();
    if (f) return f.substring(0, 2).toUpperCase();
    return '?';
  }

  userAvatarColor(firstName: string, lastName: string): string {
    const colors = [
      '#b45309', '#0369a1', '#15803d', '#7c3aed',
      '#be185d', '#c2410c', '#0f766e', '#1d4ed8',
    ];
    const name = `${firstName}${lastName}`;
    let hash = 0;
    for (let i = 0; i < name.length; i++) hash = name.charCodeAt(i) + ((hash << 5) - hash);
    return colors[Math.abs(hash) % colors.length];
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

  rolePillClass(role: string): string {
    const normalized = this.normalizeRole(role);
    if (normalized === 'owner') {
      return 'user-role-pill--owner';
    }

    if (normalized === 'manager') {
      return 'user-role-pill--manager';
    }

    if (normalized === 'staff') {
      return 'user-role-pill--staff';
    }

    return 'user-role-pill--default';
  }

  private normalizeRole(role: string): UserRoleFilter | 'unknown' {
    const normalized = role.trim().toLowerCase();
    if (normalized === 'owner') {
      return 'owner';
    }

    if (normalized === 'manager') {
      return 'manager';
    }

    if (normalized === 'staff' || normalized === 'salesperson') {
      return 'staff';
    }

    return 'unknown';
  }
}
