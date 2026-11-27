import { TestBed } from '@angular/core/testing';
import { MenuItem } from 'primeng/api';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TranslocoTestingModule } from '@ngneat/transloco';

import { MobileNavComponent } from './mobile-nav.component';
import { ShellMenuService } from './shell-menu.service';

describe('MobileNavComponent', () => {
  const childCommand = vi.fn();
  const dashboardCommand = vi.fn();

  const menuItems: MenuItem[] = [
    {
      label: 'Dashboard',
      icon: 'pi pi-home',
      command: dashboardCommand,
    },
    {
      label: 'inventory',
      items: [
        {
          label: 'Child',
          icon: 'pi pi-list',
          command: childCommand,
        },
      ],
    },
  ];

  beforeEach(() => {
    childCommand.mockReset();
    dashboardCommand.mockReset();

    TestBed.configureTestingModule({
      imports: [MobileNavComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [{ provide: ShellMenuService, useValue: { panelMenuPt: {} } }],
    });
  });

  it('opens the drawer from the hamburger trigger', () => {
    const fixture = TestBed.createComponent(MobileNavComponent);
    const component = fixture.componentInstance;
    fixture.componentRef.setInput('menuItems', menuItems);
    fixture.detectChanges();

    expect(component.isDrawerVisible()).toBe(false);

    const trigger = fixture.nativeElement.querySelector('.mobile-menu-trigger') as HTMLButtonElement;
    trigger.click();
    fixture.detectChanges();

    expect(component.isDrawerVisible()).toBe(true);
    expect(document.body.querySelector('.mobile-nav-drawer')).toBeTruthy();
  });

  it('wraps leaf commands and closes the drawer after navigation', () => {
    const fixture = TestBed.createComponent(MobileNavComponent);
    const component = fixture.componentInstance;
    fixture.componentRef.setInput('menuItems', menuItems);
    fixture.detectChanges();

    component.onDrawerVisibleChange(true);
    fixture.detectChanges();

    const wrappedDashboard = component.drawerMenuItems[0]?.command;
    expect(wrappedDashboard).toBeTruthy();

    wrappedDashboard?.({ originalEvent: new Event('click'), item: menuItems[0]! });

    expect(dashboardCommand).toHaveBeenCalledTimes(1);
    expect(component.isDrawerVisible()).toBe(false);
  });

  it('wraps nested leaf commands and closes the drawer', () => {
    const fixture = TestBed.createComponent(MobileNavComponent);
    const component = fixture.componentInstance;
    fixture.componentRef.setInput('menuItems', menuItems);
    fixture.detectChanges();

    component.onDrawerVisibleChange(true);
    fixture.detectChanges();

    const wrappedChild = component.drawerMenuItems[1]?.items?.[0]?.command;
    expect(wrappedChild).toBeTruthy();

    wrappedChild?.({
      originalEvent: new Event('click'),
      item: menuItems[1]!.items![0]!,
    });

    expect(childCommand).toHaveBeenCalledTimes(1);
    expect(component.isDrawerVisible()).toBe(false);
  });
});
