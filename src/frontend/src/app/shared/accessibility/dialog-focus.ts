import { DOCUMENT } from '@angular/common';
import { Directive, Input, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Dialog } from 'primeng/dialog';
import { fromEvent } from 'rxjs';

export function captureFocus(document: Document): HTMLElement | null {
  const element = document.activeElement;
  return element instanceof HTMLElement && element !== document.body ? element : null;
}

function canFocus(element: HTMLElement | null): element is HTMLElement {
  return (
    !!element?.isConnected &&
    !element.matches(':disabled, [aria-disabled="true"]') &&
    !element.closest('[inert], [hidden]')
  );
}

export function restoreFocus(opener: HTMLElement | null, fallback: HTMLElement | null): void {
  const target = canFocus(opener) ? opener : canFocus(fallback) ? fallback : null;
  // A stable heading is a programmatic fallback, never an extra stop in the tab order.
  if (target?.matches('h1, h2') && !target.hasAttribute('tabindex')) target.tabIndex = -1;
  target?.focus();
}

/** Restore only focus dropped by the browser when an in-dialog control becomes unavailable. */
export function rehomeLostDialogFocus(
  document: Document,
  container: HTMLElement | null | undefined,
  previous: HTMLElement,
  open: boolean,
): void {
  if (
    !open ||
    !container ||
    (document.activeElement !== document.body && document.activeElement !== previous) ||
    (previous.isConnected && !previous.matches(':disabled'))
  )
    return;
  (container.querySelector<HTMLElement>('[data-dialog-initial-focus]') ?? container).focus();
}

/** Adds initial/return focus to PrimeNG's existing modal trap; never intercepts close policy. */
@Directive({ selector: 'p-dialog[appDialogFocus]', standalone: true })
export class DialogFocusDirective {
  private readonly document = inject(DOCUMENT);
  private readonly dialog = inject(Dialog);
  private opener: HTMLElement | null = null;
  private open = false;

  @Input() set appDialogFocus(visible: boolean) {
    if (visible && !this.open) this.opener = captureFocus(this.document) ?? this.opener;
    this.open = visible;
  }

  constructor() {
    // Search deliberately blurs before removing its suggestions. Remember that last
    // initiating control while closed so opening a picker still has an exact return target.
    fromEvent(this.document, 'focusin')
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        if (!this.open && !this.dialog.container()?.contains(this.document.activeElement))
          this.opener = captureFocus(this.document);
      });
    // A source write can disable/remove its initiating control. Keep keyboard focus in
    // the existing modal when the browser drops that control's focus onto the body.
    fromEvent<FocusEvent>(this.document, 'focusout')
      .pipe(takeUntilDestroyed())
      .subscribe((event) => {
        const previous = event.target;
        if (!(previous instanceof HTMLElement) || !this.dialog.container()?.contains(previous))
          return;
        queueMicrotask(() => {
          rehomeLostDialogFocus(this.document, this.dialog.container(), previous, this.open);
        });
      });
    this.dialog.onShow.pipe(takeUntilDestroyed()).subscribe(() => {
      const container = this.dialog.container();
      const preferred = container?.querySelector<HTMLElement>('[data-dialog-initial-focus]');
      const initial = preferred?.matches('p-button')
        ? preferred.querySelector<HTMLElement>('button')
        : preferred;
      const target =
        initial ??
        container?.querySelector<HTMLElement>(
          'input:not(:disabled), [role="button"]:not([aria-disabled="true"]), button:not(:disabled)',
        ) ??
        container;
      if (target) {
        if (!target.hasAttribute('tabindex') && target === container) target.tabIndex = -1;
        target.focus();
      }
    });
    this.dialog.onHide.pipe(takeUntilDestroyed()).subscribe(() => {
      queueMicrotask(() => {
        // A reopened instance owns focus; delayed closing animation must not pull it away.
        if (this.dialog.visible) return;
        restoreFocus(
          this.opener,
          this.document.querySelector<HTMLElement>('[data-dialog-focus-fallback]') ??
            this.document.querySelector<HTMLElement>('main h1') ??
            this.document.querySelector<HTMLElement>('h1'),
        );
        this.opener = null;
      });
    });
  }
}
