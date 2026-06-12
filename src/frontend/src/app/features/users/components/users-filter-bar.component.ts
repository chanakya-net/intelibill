import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';

export type UserRoleFilter = 'all' | 'owner' | 'manager' | 'staff';

interface RoleOption {
  readonly label: string;
  readonly value: UserRoleFilter;
}

@Component({
  selector: 'app-users-filter-bar',
  standalone: true,
  imports: [FormsModule, IconFieldModule, InputIconModule, InputTextModule, SelectModule, TranslocoPipe],
  templateUrl: './users-filter-bar.component.html',
  styleUrl: './users-filter-bar.component.scss',
})
export class UsersFilterBarComponent {
  @Input({ required: true }) searchValue = '';
  @Input({ required: true }) roleFilter: UserRoleFilter = 'all';

  @Output() searchValueChange = new EventEmitter<string>();
  @Output() roleFilterChange = new EventEmitter<UserRoleFilter>();

  readonly roleOptions: RoleOption[] = [
    { label: 'common.all', value: 'all' },
    { label: 'users.owner', value: 'owner' },
    { label: 'users.manager', value: 'manager' },
    { label: 'users.staff', value: 'staff' },
  ];

  onSearchChange(value: string): void {
    this.searchValueChange.emit(value);
  }

  onRoleChange(roleFilter: UserRoleFilter): void {
    this.roleFilterChange.emit(roleFilter);
  }

  clearSearch(): void {
    this.onSearchChange('');
  }
}
