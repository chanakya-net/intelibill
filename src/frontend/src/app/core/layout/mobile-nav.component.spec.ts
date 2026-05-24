import { TestBed } from '@angular/core/testing';
import { MenuItem } from 'primeng/api';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TranslocoTestingModule } from '@ngneat/transloco';

import { MobileNavComponent } from './mobile-nav.component';

describe('MobileNavComponent', () => {
  const menuItems: MenuItem[] = [
    {
      label: 'Dashboard',
      icon: 'pi pi-home',
    },
    {
      label: 'inventory',
      items: [
        {
          label: 'Child',
          icon: 'pi pi-list',
          command: vi.fn(),
        },
      ],
    },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MobileNavComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    });
  });

  it('expands and collapses sections', () => {
    const fixture = TestBed.createComponent(MobileNavComponent);
    const component = fixture.componentInstance;
    fixture.componentInstance.menuItems = menuItems;
    fixture.detectChanges();
    fixture.componentInstance.onToggleMobileMenu();
    fixture.detectChanges();

    const sectionToggle = fixture.nativeElement.querySelector('.mobile-nav-section-toggle') as HTMLButtonElement;
    expect(sectionToggle).toBeTruthy();
    sectionToggle.click();
    fixture.detectChanges();

    expect(component.isMobileSectionExpanded('inventory')).toBe(false);

    sectionToggle.click();
    fixture.detectChanges();

    expect(component.isMobileSectionExpanded('inventory')).toBe(true);
  });

  it('emits selected leaf items', () => {
    const fixture = TestBed.createComponent(MobileNavComponent);
    fixture.componentInstance.menuItems = menuItems;
    fixture.detectChanges();
    fixture.componentInstance.onToggleMobileMenu();
    fixture.detectChanges();
    const itemSelectedSpy = vi.spyOn(fixture.componentInstance.itemSelected, 'emit');

    const dashboardButton = fixture.nativeElement.querySelector('.mobile-nav-item') as HTMLButtonElement;
    dashboardButton.click();

    expect(itemSelectedSpy).toHaveBeenCalledTimes(1);
  });

  it('closes menu after selecting a leaf item', () => {
    const fixture = TestBed.createComponent(MobileNavComponent);
    const component = fixture.componentInstance;
    fixture.componentInstance.menuItems = menuItems;
    fixture.detectChanges();
    component.onToggleMobileMenu();
    fixture.detectChanges();

    const childButton = fixture.nativeElement.querySelector('.mobile-nav-section-items .mobile-nav-item') as HTMLButtonElement;
    childButton.click();
    fixture.detectChanges();

    expect(component.isMobileMenuOpen()).toBe(false);
    expect(component.expandedMobileSectionLabel()).toBeNull();
  });
});
