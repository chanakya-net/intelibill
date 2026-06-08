import { TestBed } from '@angular/core/testing';
import { MenuItem } from 'primeng/api';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ShellMenuService } from './shell-menu.service';
import { SidebarNavComponent } from './sidebar-nav.component';

describe('SidebarNavComponent', () => {
  const childCommand = vi.fn();

  const menuItems: MenuItem[] = [
    {
      label: 'Inventory',
      items: [
        {
          label: 'Batches',
          icon: 'pi pi-list',
          command: childCommand,
        },
      ],
    },
  ];

  beforeEach(() => {
    childCommand.mockReset();

    TestBed.configureTestingModule({
      imports: [SidebarNavComponent],
      providers: [{ provide: ShellMenuService, useValue: { panelMenuPt: {} } }],
    });
  });

  it('runs submenu leaf commands and requests sidebar auto-hide', () => {
    const fixture = TestBed.createComponent(SidebarNavComponent);
    const component = fixture.componentInstance;
    const autoHideSpy = vi.fn();
    fixture.componentRef.setInput('menuItems', menuItems);
    fixture.detectChanges();
    component.autoHideSidebarRequested.subscribe(autoHideSpy);

    const wrappedChild = component.displayMenuItems[0]?.items?.[0]?.command;
    expect(wrappedChild).toBeTruthy();

    wrappedChild?.({
      originalEvent: new Event('click'),
      item: menuItems[0]!.items![0]!,
    });

    expect(childCommand).toHaveBeenCalledTimes(1);
    expect(autoHideSpy).toHaveBeenCalledTimes(1);
  });

  it('requests expansion when the collapsed sidebar is clicked', () => {
    const fixture = TestBed.createComponent(SidebarNavComponent);
    const component = fixture.componentInstance;
    const expandSpy = vi.fn();
    component.expandSidebarRequested.subscribe(expandSpy);
    fixture.componentRef.setInput('collapsed', true);
    fixture.detectChanges();

    const link = document.createElement('button');
    link.className = 'p-panelmenu-item-link';
    component.onSidebarClick({ target: link } as unknown as MouseEvent);

    expect(expandSpy).toHaveBeenCalledTimes(1);
  });
});
