import {
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  inject,
} from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { MenuItem, MenuItemCommandEvent } from 'primeng/api';
import { PanelMenuModule } from 'primeng/panelmenu';

import { ShellMenuService } from './shell-menu.service';

@Component({
  selector: 'app-sidebar-nav',
  standalone: true,
  imports: [PanelMenuModule, TranslocoPipe],
  templateUrl: './sidebar-nav.component.html',
  styleUrl: './sidebar-nav.component.scss',
})
export class SidebarNavComponent implements OnChanges {
  private readonly menuService = inject(ShellMenuService);

  @Input() menuItems: MenuItem[] = [];
  @Input() collapsed = false;
  @Output() readonly expandSidebarRequested = new EventEmitter<void>();
  @Output() readonly autoHideSidebarRequested = new EventEmitter<void>();

  readonly panelMenuPt = this.menuService.panelMenuPt;
  displayMenuItems: MenuItem[] = [];

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['menuItems']) {
      this.displayMenuItems = this.wrapMenuItems(this.menuItems);
    }
  }

  onSidebarClick(event: MouseEvent): void {
    if (!this.collapsed) {
      return;
    }

    const target = event.target as HTMLElement;
    if (target.closest('.p-panelmenu-header, .p-panelmenu-item-link')) {
      this.expandSidebarRequested.emit();
    }
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
    return (event: MenuItemCommandEvent) => {
      original?.(event);
      this.autoHideSidebarRequested.emit();
    };
  }
}
