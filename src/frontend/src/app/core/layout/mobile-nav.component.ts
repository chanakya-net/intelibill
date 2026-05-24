import { CommonModule } from '@angular/common';
import {
  Component,
  ElementRef,
  EventEmitter,
  HostListener,
  Input,
  Output,
  inject,
  signal,
} from '@angular/core';

import { MenuItem } from 'primeng/api';

@Component({
  selector: 'app-mobile-nav',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './mobile-nav.component.html',
  styleUrl: './mobile-nav.component.scss',
})
export class MobileNavComponent {
  @Input({ required: true }) menuItems: MenuItem[] = [];
  @Output() readonly itemSelected = new EventEmitter<MenuItem>();

  private readonly host = inject(ElementRef<HTMLElement>);

  readonly isMobileMenuOpen = signal(false);
  readonly expandedMobileSections = signal<Set<string>>(new Set());
  readonly expandedMobileSectionLabel = signal<string | null>(null);

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    this.closeIfOutside(event);
  }

  @HostListener('document:pointerdown', ['$event'])
  onDocumentPointerDown(event: PointerEvent): void {
    this.closeIfOutside(event);
  }

  onToggleMobileMenu(): void {
    if (this.isMobileMenuOpen()) {
      this.closeMobileMenu();
      return;
    }

    this.isMobileMenuOpen.set(true);
    this.expandedMobileSections.set(
      new Set(
        this.menuItems
          .filter((item) => item.items?.length)
          .map((item) => item.label ?? ''),
      ),
    );
    this.expandedMobileSectionLabel.set(null);
  }

  onMobileNavItemClick(item: MenuItem, parent?: MenuItem): void {
    if (item.disabled) {
      return;
    }

    if (item.items?.length) {
      if (parent) {
        this.expandedMobileSectionLabel.update((current) =>
          current === item.label ? null : (item.label ?? null),
        );
        return;
      }

      this.expandedMobileSections.update((sections) => {
        const next = new Set(sections);
        const label = item.label ?? '';

        if (next.has(label)) {
          next.delete(label);
          if (this.expandedMobileSectionLabel() === label) {
            this.expandedMobileSectionLabel.set(null);
          }
        } else {
          next.add(label);
        }

        return next;
      });
      return;
    }

    this.itemSelected.emit(item);
    item.command?.({ originalEvent: new Event('click'), item });
    this.closeMobileMenu();
  }

  isMobileSectionExpanded(label: string): boolean {
    return this.expandedMobileSections().has(label);
  }

  closeMobileMenu(): void {
    this.isMobileMenuOpen.set(false);
    this.expandedMobileSections.set(new Set());
    this.expandedMobileSectionLabel.set(null);
  }

  private closeIfOutside(event: MouseEvent | PointerEvent): void {
    if (!this.isMobileMenuOpen()) {
      return;
    }

    const target = event.target as Node | null;
    if (!target) {
      return;
    }

    const root = this.host.nativeElement;
    const composedPath = event.composedPath?.() ?? [];
    if (root.contains(target) || composedPath.includes(root)) {
      return;
    }

    this.closeMobileMenu();
  }
}
