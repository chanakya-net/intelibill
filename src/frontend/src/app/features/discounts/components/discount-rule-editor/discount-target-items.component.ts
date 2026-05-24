import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnDestroy, Output, inject, signal } from '@angular/core';
import { ReactiveFormsModule, Validators, FormBuilder } from '@angular/forms';
import { finalize, Subject, debounceTime, EMPTY, switchMap, takeUntil } from 'rxjs';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';

import { InventoryService } from '../../../inventory/services/inventory.service';
import { AvailableBatchDto } from '../../../inventory/services/inventory.models';
import { DateOnlyPipe } from '../../../../shared/pipes/date-only.pipe';

@Component({
  selector: 'app-discount-target-items',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslocoPipe, ButtonModule, InputTextModule, DateOnlyPipe],
  templateUrl: './discount-target-items.component.html',
})
export class DiscountTargetItemsComponent implements OnDestroy {
  private readonly destroy$ = new Subject<void>();
  private readonly batchSearch$ = new Subject<string>();
  private readonly formBuilder = inject(FormBuilder);
  private readonly inventoryService = inject(InventoryService);

  @Input() set selectedItemIds(value: readonly string[]) {
    const selectedId = value[0] ?? '';
    this.form.controls.inventoryBatchId.setValue(selectedId, { emitEvent: false });
    if (selectedId) {
      this.batchSearchTerm.set(selectedId);
      this.selectedBatchLabel.set(selectedId);
    } else {
      this.batchSearchTerm.set('');
      this.selectedBatchLabel.set('');
    }
  }

  @Output() readonly selectionChange = new EventEmitter<readonly string[]>();

  readonly batchSearchTerm = signal('');
  readonly batchSearchResults = signal<readonly AvailableBatchDto[]>([]);
  readonly batchSearchNoResults = signal(false);
  readonly selectedBatchLabel = signal('');
  readonly searchLoading = signal(false);
  readonly batchSearchMinCharsRequired = signal(true);

  readonly form = this.formBuilder.nonNullable.group({
    inventoryBatchId: this.formBuilder.nonNullable.control('', [Validators.required, Validators.maxLength(64)]),
  });

  constructor() {
    this.batchSearch$.pipe(
      debounceTime(300),
      switchMap((searchTerm) => {
        const trimmed = searchTerm.trim();
        this.batchSearchMinCharsRequired.set(trimmed.length < 3);

        if (trimmed.length < 3) {
          this.batchSearchResults.set([]);
          this.batchSearchNoResults.set(false);
          this.searchLoading.set(false);
          return EMPTY;
        }

        this.searchLoading.set(true);
        this.batchSearchNoResults.set(false);
        this.form.controls.inventoryBatchId.setValue('');
        return this.inventoryService.getAvailableBatchesBySearchTerm(trimmed).pipe(
          finalize(() => this.searchLoading.set(false)),
          takeUntil(this.destroy$),
        );
      }),
      takeUntil(this.destroy$),
    ).subscribe({
      next: (batches) => {
        if (batches.length === 1) {
          this.batchSearchResults.set([]);
          this.batchSearchNoResults.set(false);
          this.onSelectBatch(batches[0]);
          return;
        }

        if (batches.length === 0) {
          this.batchSearchResults.set([]);
          this.batchSearchNoResults.set(true);
          this.form.controls.inventoryBatchId.setValue('');
          this.selectedBatchLabel.set('');
          this.selectionChange.emit([]);
          return;
        }

        this.batchSearchResults.set(batches);
        this.batchSearchNoResults.set(false);
      },
      error: () => {
        this.batchSearchResults.set([]);
        this.batchSearchNoResults.set(false);
      },
    });
  }

  isValid(): boolean {
    return this.form.valid;
  }

  markAllAsTouched(): void {
    this.form.markAllAsTouched();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onBatchSearchTermChange(rawValue: string): void {
    const searchTerm = rawValue;
    this.batchSearchTerm.set(searchTerm);
    const selectedLabel = this.selectedBatchLabel();

    if (selectedLabel && searchTerm.trim() !== selectedLabel.trim()) {
      this.form.controls.inventoryBatchId.setValue('');
      this.selectedBatchLabel.set('');
      this.selectionChange.emit([]);
    }

    this.batchSearch$.next(searchTerm);
  }

  onSelectBatch(batch: AvailableBatchDto): void {
    this.form.controls.inventoryBatchId.setValue(batch.inventoryBatchId);
    this.batchSearchTerm.set(`${batch.itemName} · ${batch.batchNumber}`);
    this.selectedBatchLabel.set(`${batch.itemName} · ${batch.batchNumber}`);
    this.batchSearchResults.set([]);
    this.batchSearchNoResults.set(false);
    this.selectionChange.emit([batch.inventoryBatchId]);
  }
}
