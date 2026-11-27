import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';

import { ShopMemberDto, ShopRole } from '../../services/shop.service';

@Component({
  selector: 'app-shop-members-table',
  standalone: true,
  imports: [CommonModule, ButtonModule, TranslocoPipe],
  templateUrl: './shop-members-table.component.html',
})
export class ShopMembersTableComponent {
  @Input({ required: true }) members: readonly ShopMemberDto[] = [];
  @Input() currentUserRole = '';
  @Input() disabled = false;
  @Output() readonly roleChanged = new EventEmitter<{ userId: string; role: ShopRole }>();
  @Output() readonly memberRemoved = new EventEmitter<string>();

  readonly availableRoles: readonly ShopRole[] = ['Owner', 'Manager', 'Staff'];

  canManageMembers(): boolean {
    return this.currentUserRole.toLowerCase() === 'owner';
  }

  displayName(member: ShopMemberDto): string {
    return (
      member.fullName?.trim() ||
      [member.firstName, member.lastName].filter(Boolean).join(' ').trim() ||
      member.email?.trim() ||
      member.phoneNumber?.trim() ||
      member.userId
    );
  }

  roleLabelKey(role: string): string {
    const normalized = role.toLowerCase();
    return ['owner', 'manager', 'staff'].includes(normalized)
      ? `users.${normalized}`
      : 'shops.unknown';
  }

  onRoleChanged(userId: string, role: string): void {
    this.roleChanged.emit({ userId, role });
  }
}
