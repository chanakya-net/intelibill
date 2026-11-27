import { Component, Input, OnChanges, SimpleChanges, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { MenuItem, MenuItemCommandEvent } from 'primeng/api';
import { DrawerModule } from 'primeng/drawer';
import { PanelMenuModule } from 'primeng/panelmenu';

import { ShellMenuService } from './shell-menu.service';

@Component({
  selector: 'app-mobile-nav',
  standalone: true,
  imports: [DrawerModule, PanelMenuModule, TranslocoPipe],
  templateUrl: './mobile-nav.component.html',
  styleUrl: './mobile-nav.component.scss',
})
export class MobileNavComponent implements OnChanges {
  private readonly menuService = inject(ShellMenuService);
  private readonly translocoService = inject(TranslocoService);

  @Input() menuItems: MenuItem[] = [];
  @Input() profileMenuItems: MenuItem[] = [];
  @Input() profileInitials = 'U';
  @Input() profileDisplayName = '';

  readonly isDrawerVisible = signal(false);
  readonly panelMenuPt = this.menuService.panelMenuPt;
  drawerMenuItems: MenuItem[] = [];
  profileDrawerMenuItems: MenuItem[] = [];

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['menuItems']) {
      this.drawerMenuItems = this.wrapMenuItems(this.menuItems);
    }

    if (changes['profileMenuItems'] || changes['profileDisplayName']) {
      const wrappedItems = this.wrapMenuItems(this.profileMenuItems);
      const profileName = this.profileDisplayName.trim() || this.translocoService.translate('shell.userFallback');
      const profileCaption = this.translocoService.translate('shell.profile');
      this.profileDrawerMenuItems = wrappedItems.length > 0
        ? [{
            label: `${profileName}\n${profileCaption}`,
            icon: 'pi pi-user',
            items: wrappedItems,
            expanded: false,
          }]
        : [];
    }
  }

  onToggleMobileMenu(): void {
    this.isDrawerVisible.update((open) => !open);
  }

  onDrawerVisibleChange(visible: boolean): void {
    this.isDrawerVisible.set(visible);
  }

  private wrapMenuItems(items: MenuItem[]): MenuItem[] {
    return items.map((item) => {
      const childItems = item.items?.length ? this.wrapMenuItems(item.items) : undefined;
      const hasChildren = !!childItems?.length;

      return {
        ...item,
        items: childItems,
        command: hasChildren ? item.command : this.wrapLeafCommand(item.command),
      };
    });
  }

  private wrapLeafCommand(original?: MenuItem['command']): MenuItem['command'] {
    if (!original) {
      return () => this.isDrawerVisible.set(false);
    }

    return (event: MenuItemCommandEvent) => {
      original(event);
      this.isDrawerVisible.set(false);
    };
  }
}
