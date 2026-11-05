import { Component, computed, inject, input, linkedSignal, output } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { InputTextModule } from 'primeng/inputtext';

import {
  QuantityInputError,
  formatQuantity,
  parseQuantityInput,
  quantityStep,
} from './quantity-input';

type QuantityFieldError = QuantityInputError | 'exceedsMax';

/**
 * A quantity typed as text and read exactly in the item's unit. It commits on blur or Enter; a value the unit cannot
 * hold (too many decimals, zero, malformed, more than `max`) is refused with a message naming the unit, the typed
 * text stays for correction, and nothing is emitted. A valid value that differs from `value` is emitted once.
 */
@Component({
  selector: 'app-quantity-field',
  standalone: true,
  imports: [InputTextModule, TranslocoPipe],
  templateUrl: './quantity-field.component.html',
  styleUrl: './quantity-field.component.scss',
})
export class QuantityFieldComponent {
  private readonly transloco = inject(TranslocoService);
  private readonly locale = toSignal(this.transloco.langChanges$, {
    initialValue: this.transloco.getActiveLang(),
  });

  readonly value = input.required<number>();
  /** Decimal places the unit allows (0 = whole units). */
  readonly scale = input(0);
  /** The most that may be entered (the stock on hand), or null when there is no limit. */
  readonly max = input<number | null>(null);
  /** The unit shown beside the field and named in its messages. */
  readonly unitLabel = input('');
  readonly inputId = input.required<string>();
  /** Translated accessible name of the field. */
  readonly ariaLabel = input.required<string>();

  readonly committed = output<number>();

  /** Text typed since the last commit; null while the field shows `value`. A new `value` replaces it. */
  private readonly draft = linkedSignal<number, string | null>({ source: this.value, computation: () => null });
  /** Why the last commit was refused; cleared when the value, unit or limit changes. */
  protected readonly error = linkedSignal<unknown, QuantityFieldError | null>({
    source: () => [this.value(), this.scale(), this.max()],
    computation: () => null,
  });

  protected readonly text = computed(
    () => this.draft() ?? formatQuantity(this.value(), this.scale(), this.locale()),
  );
  protected readonly inputMode = computed(() => (this.scale() > 0 ? 'decimal' : 'numeric'));
  protected readonly errorId = computed(() => `${this.inputId()}-error`);
  protected readonly errorParams = computed(() => {
    const scale = this.scale();
    const max = this.max();
    return {
      decimals: scale,
      step: formatQuantity(quantityStep(scale), scale, this.locale()),
      max: max === null ? '' : formatQuantity(max, scale, this.locale()),
    };
  });

  protected onInput(event: Event): void {
    this.draft.set((event.target as HTMLInputElement).value);
  }

  protected onEnter(event: Event): void {
    event.preventDefault();
    this.commit();
  }

  /**
   * Reads the shown text and emits it when it differs from `value`. Resolves false, showing why, when the text cannot
   * be this unit's quantity; callers about to act on `value` (e.g. an add button) check it first.
   */
  commit(): boolean {
    const parsed = parseQuantityInput(this.text(), this.locale(), this.scale());
    if (!parsed.ok) {
      this.error.set(parsed.error);
      return false;
    }

    const max = this.max();
    if (max !== null && parsed.value > max) {
      this.error.set('exceedsMax');
      return false;
    }

    this.error.set(null);
    this.draft.set(null);
    if (parsed.value !== this.value()) {
      this.committed.emit(parsed.value);
    }
    return true;
  }

  protected errorKey(error: QuantityFieldError): string {
    if (error === 'tooPrecise') {
      return this.scale() > 0 ? 'common.quantity.errors.tooPrecise' : 'common.quantity.errors.tooPreciseWhole';
    }
    return QUANTITY_FIELD_ERROR_KEYS[error];
  }
}

const QUANTITY_FIELD_ERROR_KEYS: Readonly<Record<Exclude<QuantityFieldError, 'tooPrecise'>, string>> = {
  empty: 'common.quantity.errors.empty',
  invalid: 'common.quantity.errors.invalid',
  notPositive: 'common.quantity.errors.notPositive',
  exceedsMax: 'common.quantity.errors.exceedsMax',
};
