import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject } from '@angular/core';
import { TranslocoService } from '@ngneat/transloco';
import { MenuItem } from 'primeng/api';
import { PanelMenuModule } from 'primeng/panelmenu';

import { ShellMenuService } from './shell-menu.service';

@Component({
  selector: 'app-sidebar-profile-menu',
  standalone: true,
  imports: [PanelMenuModule],
  templateUrl: './sidebar-profile-menu.component.html',
  styleUrl: './sidebar-profile-menu.component.scss',
})
export class SidebarProfileMenuComponent implements OnChanges {
  private readonly menuService = inject(ShellMenuService);
  private readonly translocoService = inject(TranslocoService);

  @Input() menuItems: MenuItem[] = [];
  @Input() collapsed = false;
  @Input() profileInitials = 'U';
  @Input() profileDisplayName = '';

  @Output() readonly expandSidebarRequested = new EventEmitter<void>();

  readonly panelMenuPt = this.menuService.panelMenuPt;
  displayMenuItems: MenuItem[] = [];

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['menuItems'] || changes['profileDisplayName']) {
      this.displayMenuItems = this.buildDisplayMenuItems();
    }
  }

  onProfileSectionClick(event: MouseEvent): void {
    if (!this.collapsed) {
      return;
    }

    const target = event.target as HTMLElement;
    if (target.closest('.p-panelmenu-header, .p-panelmenu-item-link')) {
      this.expandSidebarRequested.emit();
    }
  }

  private buildDisplayMenuItems(): MenuItem[] {
    if (this.menuItems.length === 0) {
      return [];
    }

    const profileName = this.profileDisplayName.trim()
      || this.translocoService.translate('shell.userFallback');
    const profileCaption = this.translocoService.translate('shell.profile');

    return [
      {
        label: `${profileName}\n${profileCaption}`,
        icon: 'pi pi-user',
        items: [...this.menuItems],
        expanded: false,
      },
    ];
  }
}
