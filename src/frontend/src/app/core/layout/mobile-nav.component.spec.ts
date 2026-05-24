import { TestBed } from '@angular/core/testing';
import { MenuItem } from 'primeng/api';

import { describe, expect, it, vi } from 'vitest';

import { MobileNavComponent } from './mobile-nav.component';

describe('MobileNavComponent', () => {
  const dashboardCommand = vi.fn();
  const addProductCommand = vi.fn();
  const logoutCommand = vi.fn();

  const menuItems: MenuItem[] = [
    {
      label: 'Dashboard',
      icon: 'pi pi-home',
      command: dashboardCommand,
    },
    {
      label: 'Inventory',
      icon: 'pi pi-box',
      items: [
        {
          label: 'Add product',
          icon: 'pi pi-plus-circle',
          command: addProductCommand,
        },
      ],
    },
    {
      label: 'Profile',
      icon: 'pi pi-cog',
      items: [
        {
          label: 'Language',
          icon: 'pi pi-globe',
          items: [
            {
              label: 'English',
              icon: 'pi pi-check',
              command: logoutCommand,
            },
          ],
        },
      ],
    },
  ];

  function setup() {
    TestBed.configureTestingModule({
      imports: [MobileNavComponent],
    });

    const fixture = TestBed.createComponent(MobileNavComponent);
    fixture.componentRef.setInput('menuItems', menuItems);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    dashboardCommand.mockReset();
    addProductCommand.mockReset();
    logoutCommand.mockReset();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('toggles the mobile menu and expands nested sections', () => {
    const fixture = setup();
    const component = fixture.componentInstance;

    expect(component.isMobileMenuOpen()).toBe(false);

    fixture.nativeElement.querySelector('.mobile-menu-trigger')?.click();
    fixture.detectChanges();

    expect(component.isMobileMenuOpen()).toBe(true);
    expect(fixture.nativeElement.querySelector('.mobile-nav')).not.toBeNull();
    expect(component.isMobileSectionExpanded('Inventory')).toBe(true);

    fixture.nativeElement.querySelector('.mobile-nav-section-toggle')?.click();
    fixture.detectChanges();

    expect(component.isMobileSectionExpanded('Inventory')).toBe(false);
  });

  it('emits selected leaf items and closes the menu', () => {
    const fixture = setup();
    const component = fixture.componentInstance;
    const selected: MenuItem[] = [];
    component.itemSelected.subscribe((item) => selected.push(item));

    fixture.nativeElement.querySelector('.mobile-menu-trigger')?.click();
    fixture.detectChanges();
    const languageButton = Array.from(
      fixture.nativeElement.querySelectorAll('.mobile-nav-item') as NodeListOf<HTMLElement>,
    ).find((button) => button.textContent?.includes('Language'));
    languageButton?.click();
    fixture.detectChanges();

    const englishButton = Array.from(
      fixture.nativeElement.querySelectorAll('.mobile-nav-subitem') as NodeListOf<HTMLElement>,
    ).find((button) => button.textContent?.includes('English'));
    englishButton?.click();
    fixture.detectChanges();

    expect(selected).toHaveLength(1);
    expect(selected[0]?.label).toBe('English');
    expect(logoutCommand).toHaveBeenCalledTimes(1);
    expect(component.isMobileMenuOpen()).toBe(false);
    expect(component.expandedMobileSectionLabel()).toBeNull();
  });
});
