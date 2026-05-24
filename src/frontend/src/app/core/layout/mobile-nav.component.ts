import {
  Component,
  ElementRef,
  EventEmitter,
  HostListener,
  Input,
  Output,
  ViewChild,
  signal,
} from '@angular/core';
import { MenuItem } from 'primeng/api';
import { TranslocoPipe } from '@ngneat/transloco';

@Component({
  selector: 'app-mobile-nav',
  standalone: true,
  imports: [TranslocoPipe],
  templateUrl: './mobile-nav.component.html',
  styleUrl: './mobile-nav.component.scss',
})
export class MobileNavComponent {
  @Input() menuItems: MenuItem[] = [];
  @Output() readonly itemSelected = new EventEmitter<MenuItem>();

  @ViewChild('mobileNavRef') mobileNavRef?: ElementRef<HTMLElement>;
  @ViewChild('mobileMenuTrigger') mobileMenuTrigger?: ElementRef<HTMLElement>;

  readonly isMobileMenuOpen = signal(false);
  readonly expandedMobileSections = signal<Set<string>>(new Set(['inventory', 'profile', 'sales']));
  readonly expandedMobileSectionLabel = signal<string | null>(null);

  isMobileSectionExpanded(key: string): boolean {
    return this.expandedMobileSections().has(key);
  }

  onToggleMobileSection(key: string): void {
    this.expandedMobileSections.update((sections) => {
      const next = new Set(sections);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }

      return next;
    });
  }

  onToggleMobileMenu(): void {
    this.isMobileMenuOpen.update((open) => !open);
    if (!this.isMobileMenuOpen()) {
      this.expandedMobileSectionLabel.set(null);
    }
  }

  onMobileNavItemClick(item: MenuItem): void {
    if (item.disabled) {
      return;
    }

    if (item.items?.length) {
      const label = item.label ?? null;
      this.expandedMobileSectionLabel.update((current) => (current === label ? null : label));
      return;
    }

    this.itemSelected.emit(item);
    this.closeMobileMenu();
  }

  closeMobileMenu(): void {
    this.isMobileMenuOpen.set(false);
    this.expandedMobileSectionLabel.set(null);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    this.closeForEvent(event);
  }

  @HostListener('document:pointerdown', ['$event'])
  onDocumentPointerDown(event: PointerEvent): void {
    this.closeForEvent(event);
  }

  private closeForEvent(event: MouseEvent | PointerEvent): void {
    const target = event.target as Node | null;
    if (!target) {
      return;
    }

    if (!this.isMobileMenuOpen()) {
      return;
    }

    const composedPath = event.composedPath?.() ?? [];
    const isInNav = this.mobileNavRef?.nativeElement
      ? this.isTargetInside(this.mobileNavRef.nativeElement, target, composedPath)
      : false;
    const isInTrigger = this.mobileMenuTrigger?.nativeElement
      ? this.isTargetInside(this.mobileMenuTrigger.nativeElement, target, composedPath)
      : false;

    if (!isInNav && !isInTrigger) {
      this.closeMobileMenu();
    }
  }

  private isTargetInside(
    root: HTMLElement,
    target: Node,
    composedPath: readonly EventTarget[],
  ): boolean {
    return root.contains(target) || composedPath.includes(root);
  }
}
